using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Memento.Audio.Sources;

/// <summary>
/// Raises <see cref="Changed"/> (debounced, on a thread-pool thread) when a device is added, removed, enabled or
/// disabled, the default device changes (<c>IMMNotificationClient</c>), or an app opens an audio session on any
/// active output (<c>OnSessionCreated</c>). All COM registration happens on MTA thread-pool threads, which
/// session notifications require.
/// </summary>
public sealed partial class AudioSourceWatcher : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly object _sync = new();
    private readonly TimeSpan _debounce;
    private readonly ILogger _logger;
    private readonly Timer _timer;
    private readonly List<(MMDevice Device, AudioSessionManager.SessionCreatedDelegate Handler)> _sessionManagers = [];
    private MMDeviceEnumerator? _enumerator;
    private EndpointCallback? _callback;
    private AudioSourceChanges _pending;
    private bool _resubscribe;
    private bool _disposed;

    private AudioSourceWatcher(TimeSpan debounce, ILogger logger)
    {
        _debounce = debounce;
        _logger = logger;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<AudioSourcesChangedEventArgs>? Changed;

    /// <summary>Registers for notifications. Does not block the calling (possibly UI) thread on COM work.</summary>
    public static async Task<AudioSourceWatcher> StartAsync(TimeSpan? debounce = null, ILogger<AudioSourceWatcher>? logger = null, CancellationToken cancellationToken = default)
    {
        var watcher = new AudioSourceWatcher(debounce ?? DefaultDebounce, (ILogger?)logger ?? NullLogger.Instance);
        await Task.Run(watcher.Subscribe, cancellationToken).ConfigureAwait(false);
        return watcher;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
        if (_enumerator is not null && _callback is not null)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_callback);
            }
            catch (COMException)
            {
            }
        }

        UnsubscribeSessions();
        _enumerator?.Dispose();
    }

    internal void Notify(AudioSourceChanges change, bool devicesChanged)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pending |= change;
            _resubscribe |= devicesChanged;

            // Restart the window: callbacks must return quickly and must not touch the enumerator themselves.
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Subscribe()
    {
        _enumerator = new MMDeviceEnumerator();
        _callback = new EndpointCallback(this);
        _enumerator.RegisterEndpointNotificationCallback(_callback);
        SubscribeSessions();
    }

    private void SubscribeSessions()
    {
        if (_enumerator is null)
        {
            return;
        }

        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                AudioSessionManager.SessionCreatedDelegate handler = (_, _) => Notify(AudioSourceChanges.SessionCreated, devicesChanged: false);
                device.AudioSessionManager.OnSessionCreated += handler;
                lock (_sync)
                {
                    _sessionManagers.Add((device, handler));
                }
            }
            catch (COMException ex)
            {
                LogSubscribeFailed(_logger, ex.HResult);
                device.Dispose();
            }
        }
    }

    private void UnsubscribeSessions()
    {
        List<(MMDevice Device, AudioSessionManager.SessionCreatedDelegate Handler)> old;
        lock (_sync)
        {
            old = [.. _sessionManagers];
            _sessionManagers.Clear();
        }

        foreach (var (device, handler) in old)
        {
            try
            {
                device.AudioSessionManager.OnSessionCreated -= handler;
            }
            catch (COMException)
            {
            }

            device.Dispose();
        }
    }

    private void Flush()
    {
        AudioSourceChanges changes;
        bool resubscribe;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            changes = _pending;
            resubscribe = _resubscribe;
            _pending = AudioSourceChanges.None;
            _resubscribe = false;
        }

        if (resubscribe)
        {
            UnsubscribeSessions();
            SubscribeSessions();
        }

        if (changes != AudioSourceChanges.None)
        {
            Changed?.Invoke(this, new AudioSourcesChangedEventArgs(changes));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not subscribe to audio session notifications on an output device (0x{HResult:X8})")]
    private static partial void LogSubscribeFailed(ILogger logger, int hResult);

    /// <summary>The <c>IMMNotificationClient</c> callback object; only forwards to the debouncer.</summary>
    [ComVisible(true)]
    private sealed class EndpointCallback(AudioSourceWatcher owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => owner.Notify(AudioSourceChanges.DeviceStateChanged, devicesChanged: true);

        public void OnDeviceAdded(string pwstrDeviceId) => owner.Notify(AudioSourceChanges.DeviceAdded, devicesChanged: true);

        public void OnDeviceRemoved(string deviceId) => owner.Notify(AudioSourceChanges.DeviceRemoved, devicesChanged: true);

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (role == Role.Console)
            {
                owner.Notify(AudioSourceChanges.DefaultDeviceChanged, devicesChanged: false);
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // Fires constantly (volume, format); not a source-list change.
        }
    }
}
