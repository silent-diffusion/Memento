using System.Runtime.InteropServices;
using System.Text;
using NAudio.MediaFoundation;

namespace Memento.Audio.Codecs;

/// <summary>
/// Starts Media Foundation once per process and finds the built-in encoder MFTs. Windows Server images without
/// the Media Foundation feature (some CI runners) report <see cref="IsAvailable"/> = false.
/// </summary>
public static class MediaFoundationRuntime
{
    /// <summary><c>MFAudioFormat_FLAC</c>.</summary>
    public static readonly Guid FlacSubtype = new("0000F1AC-0000-0010-8000-00AA00389B71");

    /// <summary><c>MFTranscodeContainerType_FLAC</c>; NAudio infers containers from the extension, which fails for .flac.</summary>
    public static readonly Guid FlacContainer = new("31344aa3-05a9-42b5-901b-8e9d4257f75e");

    private static readonly object Gate = new();
    private static readonly Lazy<bool> Available = new(Probe);
    private static bool _started;

    /// <summary>True when Media Foundation starts and the FLAC encoder MFT exists.</summary>
    public static bool IsAvailable => Available.Value;

    internal static void EnsureStarted()
    {
        lock (Gate)
        {
            if (!_started)
            {
                MediaFoundationApi.Startup();
                _started = true;
            }
        }
    }

    /// <summary>
    /// Activates the encoder MFT whose output subtype is <paramref name="subtype"/> (friendly name first, then by
    /// probing each audio encoder). The caller shuts the activate down and releases both.
    /// </summary>
    internal static (IMFActivate Activate, IMFTransform Transform)? ActivateEncoder(Guid subtype, string nameHint)
    {
        EnsureStarted();
        var candidates = MediaFoundationApi.EnumerateTransforms(MediaFoundationTransformCategories.AudioEncoder).ToList();
        try
        {
            var ordered = candidates
                .OrderByDescending(a => FriendlyName(a).Contains(nameHint, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var activate in ordered)
            {
                var outputs = OutputSubtypes(activate);
                if (outputs.Count > 0 && !outputs.Contains(subtype))
                {
                    continue;
                }

                if (outputs.Count == 0 && !FriendlyName(activate).Contains(nameHint, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                activate.ActivateObject(typeof(IMFTransform).GUID, out var instance);
                candidates.Remove(activate);
                return (activate, (IMFTransform)instance);
            }

            return null;
        }
        finally
        {
            foreach (var other in candidates)
            {
                Marshal.ReleaseComObject(other);
            }
        }
    }

    internal static void Release(IMFActivate activate, IMFTransform transform)
    {
        try
        {
            activate.ShutdownObject();
        }
        catch (COMException)
        {
        }

        Marshal.ReleaseComObject(transform);
        Marshal.ReleaseComObject(activate);
    }

    internal static string FriendlyName(IMFActivate activate)
    {
        try
        {
            activate.GetStringLength(MediaFoundationAttributes.MFT_FRIENDLY_NAME_Attribute, out var length);
            var sb = new StringBuilder(length + 1);
            activate.GetString(MediaFoundationAttributes.MFT_FRIENDLY_NAME_Attribute, sb, sb.Capacity, out _);
            return sb.ToString();
        }
        catch (COMException)
        {
            return string.Empty;
        }
    }

    /// <summary>Output subtypes the MFT registered (<c>MFT_OUTPUT_TYPES_Attributes</c>: pairs of major/sub GUIDs).</summary>
    private static List<Guid> OutputSubtypes(IMFActivate activate)
    {
        var result = new List<Guid>();
        try
        {
            activate.GetBlobSize(MediaFoundationAttributes.MFT_OUTPUT_TYPES_Attributes, out var size);
            if (size < 32)
            {
                return result;
            }

            var blob = new byte[size];
            activate.GetBlob(MediaFoundationAttributes.MFT_OUTPUT_TYPES_Attributes, blob, size, out _);
            for (var offset = 0; offset + 32 <= size; offset += 32)
            {
                result.Add(new Guid(blob.AsSpan(offset + 16, 16)));
            }
        }
        catch (COMException)
        {
        }

        return result;
    }

    private static bool Probe()
    {
        try
        {
            var flac = ActivateEncoder(FlacSubtype, "FLAC");
            if (flac is null)
            {
                return false;
            }

            Release(flac.Value.Activate, flac.Value.Transform);
            return true;
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            return false;
        }
    }
}
