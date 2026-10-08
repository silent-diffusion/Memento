using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Memento.Audio.Sources;

/// <summary>
/// Lists what can be recorded: microphones (active capture endpoints), system loopback (active render endpoints,
/// the default flagged) and applications with audio sessions on any active render endpoint (deduplicated by PID,
/// the system-sounds session hidden). Re-enumerates on every call; cheap enough for <c>sources.list</c>.
/// </summary>
public sealed partial class AudioSourceEnumerator(ILogger<AudioSourceEnumerator>? logger = null)
{
    /// <summary><c>PKEY_Device_EnumeratorName</c>: the bus the device hangs off ("USB", "BTHENUM", "HDAUDIO", …).</summary>
    private static readonly PropertyKey EnumeratorNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public const string SystemDefaultName = "Everything this PC plays";

    public const string ApplicationDetail = "Only this app";

    private const string WebView2ProcessName = "msedgewebview2";

    public IReadOnlyList<AudioSourceInfo> List(AudioSourceListOptions? options = null)
    {
        options ??= AudioSourceListOptions.Default;
        var result = new List<AudioSourceInfo>();
        using var enumerator = new MMDeviceEnumerator();
        result.AddRange(Microphones(enumerator));
        var renders = Renders(enumerator);
        try
        {
            result.AddRange(renders.Select(r => r.Info));
            result.AddRange(Applications(renders.Select(r => r.Device), options));
        }
        finally
        {
            foreach (var r in renders)
            {
                r.Device.Dispose();
            }
        }

        return result;
    }

    /// <summary>Describes one source without listing everything (names a track); null if it no longer exists.</summary>
    public static AudioSourceInfo? Describe(AudioSourceId id)
    {
        if (id.Kind == AudioSourceKind.Application)
        {
            return DescribeProcess(id.ProcessId, displayName: null, options: AudioSourceListOptions.Default);
        }

        using var enumerator = new MMDeviceEnumerator();
        try
        {
            using var device = enumerator.GetDevice(id.EndpointId);
            if (device.State != DeviceState.Active)
            {
                return null;
            }

            var flow = id.Kind == AudioSourceKind.Microphone ? DataFlow.Capture : DataFlow.Render;
            var isDefault = DefaultId(enumerator, flow) == device.ID;
            return id.Kind == AudioSourceKind.Microphone ? MicrophoneInfo(device, isDefault) : SystemInfo(device, isDefault);
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Human label for the bus a device is on.</summary>
    internal static string BusLabel(string? enumeratorName) => enumeratorName?.ToUpperInvariant() switch
    {
        "USB" => "USB",
        "BTHENUM" or "BTHHFENUM" or "BTHLEDEVICE" or "BTHLE" => "Bluetooth",
        "HDAUDIO" or "INTELAUDIO" or "ACP" or "AMDACP" or "SWD" or "PCI" => "Built-in",
        "ROOT" => "Virtual device",
        "DISPLAY" or "HDMI" => "Display audio",
        _ => "Audio device",
    };

    private static IEnumerable<AudioSourceInfo> Microphones(MMDeviceEnumerator enumerator)
    {
        var defaultId = DefaultId(enumerator, DataFlow.Capture);
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
            {
                yield return MicrophoneInfo(device, device.ID == defaultId);
            }
        }
    }

    private static List<(MMDevice Device, AudioSourceInfo Info)> Renders(MMDeviceEnumerator enumerator)
    {
        var defaultId = DefaultId(enumerator, DataFlow.Render);
        var list = new List<(MMDevice, AudioSourceInfo)>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            list.Add((device, SystemInfo(device, device.ID == defaultId)));
        }

        // Default output first, as the UI offers it first.
        return [.. list.OrderByDescending(x => x.Item2.IsDefault)];
    }

    private static AudioSourceInfo MicrophoneInfo(MMDevice device, bool isDefault) =>
        new(AudioSourceId.Microphone(device.ID).ToString(), AudioSourceKind.Microphone, SafeName(device), BusLabel(Bus(device)), isDefault, null)
        {
            EndpointId = device.ID,
        };

    private static AudioSourceInfo SystemInfo(MMDevice device, bool isDefault)
    {
        var name = SafeName(device);
        return new AudioSourceInfo(
            AudioSourceId.System(device.ID).ToString(),
            AudioSourceKind.System,
            isDefault ? SystemDefaultName : name,
            isDefault ? $"Default output, {name}" : $"Output device, {BusLabel(Bus(device))}",
            isDefault,
            null)
        {
            EndpointId = device.ID,
        };
    }

    private List<AudioSourceInfo> Applications(IEnumerable<MMDevice> renders, AudioSourceListOptions options)
    {
        var seen = new Dictionary<int, (string? DisplayName, bool Active)>();
        foreach (var device in renders)
        {
            try
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.IsSystemSoundsSession || session.State == AudioSessionState.AudioSessionStateExpired)
                    {
                        continue;
                    }

                    var pid = (int)session.GetProcessID;
                    if (pid == 0 || (options.ExcludeOwnProcess && IsOwnProcess(pid)))
                    {
                        continue;
                    }

                    var active = session.State == AudioSessionState.AudioSessionStateActive;
                    var display = AppIdentity.CleanDisplayName(session.DisplayName);
                    if (!seen.TryGetValue(pid, out var existing) || (active && !existing.Active) || (existing.DisplayName is null && display is not null))
                    {
                        seen[pid] = (display ?? existing.DisplayName, active || existing.Active);
                    }
                }
            }
            catch (COMException ex)
            {
                // The endpoint id, not the friendly name: names often carry a person's name ("Alex's AirPods").
                LogSessionsFailed(_logger, SafeId(device), ex.HResult);
            }
        }

        var apps = new List<AudioSourceInfo>();
        foreach (var (pid, (display, _)) in seen.OrderByDescending(kv => kv.Value.Active))
        {
            var info = DescribeProcess(pid, display, options);
            if (info is not null)
            {
                apps.Add(info);
            }
        }

        return apps;
    }

    /// <summary>
    /// Memento itself, or one of the WebView2 processes it runs (Review plays audio from one). Other programs Memento
    /// happened to start, such as a browser opened from a link, are still offered.
    /// </summary>
    internal static bool IsOwnProcess(int pid)
    {
        if (pid == Environment.ProcessId)
        {
            return true;
        }

        var image = ProcessInfoNative.QueryImagePath(pid);
        return image is not null
            && string.Equals(Path.GetFileNameWithoutExtension(image), WebView2ProcessName, StringComparison.OrdinalIgnoreCase)
            && ProcessInfoNative.IsInTreeOf(pid, Environment.ProcessId);
    }

    private static AudioSourceInfo? DescribeProcess(int pid, string? displayName, AudioSourceListOptions options)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return null; // Exited since the session was listed.
        }

        using (process)
        {
            string processName;
            try
            {
                processName = process.ProcessName;
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            var exe = AppIdentity.ExecutablePath(process);
            var name = AppIdentity.ResolveName(displayName, AppIdentity.FileDescription(exe), processName, pid);
            return new AudioSourceInfo(AudioSourceId.Application(pid).ToString(), AudioSourceKind.Application, name, ApplicationDetail, false, pid)
            {
                ProcessName = processName,
                IconPng = options.IncludeIcons ? AppIdentity.IconPng(exe) : null,
            };
        }
    }

    private static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
            {
                return null;
            }

            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
            return device.ID;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string SafeName(MMDevice device)
    {
        try
        {
            return device.FriendlyName;
        }
        catch (COMException)
        {
            return "Audio device";
        }
    }

    private static string SafeId(MMDevice device)
    {
        try
        {
            return device.ID;
        }
        catch (COMException)
        {
            return "an audio endpoint";
        }
    }

    private static string? Bus(MMDevice device)
    {
        try
        {
            var store = device.Properties;
            return store.Contains(EnumeratorNameKey) ? store[EnumeratorNameKey].Value as string : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotImplementedException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not list audio sessions on endpoint {EndpointId} (0x{HResult:X8})")]
    private static partial void LogSessionsFailed(ILogger logger, string endpointId, int hResult);
}
