using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Memento.Audio.Capture.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Audio.Capture;

/// <summary>
/// The one shared, event-driven WASAPI capture loop (our own interop) for all three source kinds:
/// endpoint capture (<c>IMMDevice::Activate</c>), endpoint loopback (<c>AUDCLNT_STREAMFLAGS_LOOPBACK</c>) and
/// process loopback (<c>ActivateAudioInterfaceAsync</c> with <c>PROCESS_LOOPBACK</c> + <c>INCLUDE_TARGET_PROCESS_TREE</c>).
/// <para>
/// Everything COM happens on a dedicated MTA thread at <see cref="ThreadPriority.Highest"/> registered with MMCSS,
/// so the caller's apartment does not matter and the caller is never blocked. Packets carry QPC and device
/// positions; the thread never waits on the consumer (a full channel drops and counts the packet).
/// </para>
/// </summary>
public sealed partial class WasapiAudioCapture : IAudioCapture
{
    private const int NoLoss = -1;

    private readonly CaptureOptions _options;
    private readonly ILogger _logger;
    private readonly Channel<CapturePacket> _channel;
    private readonly CaptureCounters _counters = new();
    private readonly Thread _thread;
    private readonly AutoResetEvent _bufferEvent = new(false);
    private readonly ManualResetEventSlim _go = new(false);
    private readonly TaskCompletionSource<AudioFormat> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _watchedProcess;
    private AudioFormat? _format;
    private volatile bool _startRequested;
    private volatile bool _stopRequested;
    private int _pendingLoss = NoLoss;
    private long _startedAtQpc;
    private long _pendingDropped;
    private long _lastEndQpc;
    private CaptureLostEventArgs? _loss;
    private int _disposed;

    /// <summary>A stream whose capture thread has not started (<see cref="OpenAsync"/> starts it; tests drive <see cref="Drain"/> directly).</summary>
    internal WasapiAudioCapture(AudioSourceId source, CaptureOptions options, ILogger logger)
    {
        Source = source;
        _options = options;
        _logger = logger;
        _channel = Channel.CreateBounded<CapturePacket>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait, // TryWrite then fails instead of blocking.
        });
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = $"Memento capture ({source.Kind})",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    public AudioSourceId Source { get; }

    public AudioFormat Format => _format ?? throw new InvalidOperationException("The stream is not open yet.");

    public ChannelReader<CapturePacket> Packets => _channel.Reader;

    public CaptureStatistics Statistics => _counters.Snapshot();

    public long StartedAtQpc => Volatile.Read(ref _startedAtQpc);

    public CaptureLostEventArgs? Loss => Volatile.Read(ref _loss);

    public event EventHandler<CaptureLostEventArgs>? Lost;

    /// <summary>
    /// Activates and initialises <paramref name="source"/> on a new capture thread. Completes when the stream is
    /// ready to start; never blocks the calling thread.
    /// </summary>
    /// <exception cref="AudioSourceUnavailableException">The source cannot be captured; the message says why.</exception>
    public static async Task<WasapiAudioCapture> OpenAsync(
        AudioSourceId source,
        CaptureOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var stream = new WasapiAudioCapture(source, options ?? CaptureOptions.Default, logger ?? NullLogger.Instance);
        stream._thread.Start();
        try
        {
            stream._format = await stream._ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_ready.Task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Open the stream before starting it.");
        }

        _startRequested = true;
        _go.Set();
    }

    public Task StopAsync()
    {
        _stopRequested = true;
        _go.Set();
        try
        {
            _bufferEvent.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        return _thread.IsAlive || _thread.ThreadState == System.Threading.ThreadState.Unstarted
            ? _exited.Task
            : Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        while (_channel.Reader.TryRead(out var leftover))
        {
            leftover.Release();
        }

        _watchedProcess?.Dispose();
        _bufferEvent.Dispose();
        _go.Dispose();
    }

    private void Run()
    {
        CoreAudio.IAudioClient? client = null;
        CoreAudio.IAudioCaptureClient? capture = null;
        CoreAudio.IAudioSessionControl? session = null;
        SessionEventsSink? sink = null;
        var mmcss = IntPtr.Zero;
        try
        {
            try
            {
                var sw = Stopwatch.StartNew();
                (client, var format, var bufferDuration) = Source.Kind == AudioSourceKind.Application ? ActivateProcessLoopback() : ActivateEndpoint();
                Check(client.SetEventHandle(_bufferEvent.SafeWaitHandle.DangerousGetHandle()), "SetEventHandle");
                var iid = CoreAudio.IidAudioCaptureClient;
                Check(client.GetService(ref iid, out var service), "GetService(IAudioCaptureClient)");
                capture = (CoreAudio.IAudioCaptureClient)service;
                (session, sink) = TryRegisterSessionEvents(client);
                client.GetBufferSize(out var bufferFrames);
                _format = format;
                LogOpened(_logger, Source, format, bufferFrames, bufferDuration.TotalMilliseconds, sw.ElapsedMilliseconds);
                _ready.TrySetResult(format);
            }
#pragma warning disable CA1031 // Every activation failure is reported to the opener as a specific exception.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _ready.TrySetException(ex as AudioSourceUnavailableException ?? Unavailable(ex));
                return;
            }

            _go.Wait();
            if (_stopRequested || !_startRequested)
            {
                return;
            }

            mmcss = RegisterMmcss();
            var hr = client.Start();
            Volatile.Write(ref _startedAtQpc, QpcClock.Now);
            if (hr < 0)
            {
                ReportLoss(MapLoss(hr), hr);
                return;
            }

            Loop(capture!, _format!);
        }
        finally
        {
            Cleanup(client, capture, session, sink, mmcss);
            _channel.Writer.TryComplete();
            _exited.TrySetResult();
        }
    }

    private void Loop(CoreAudio.IAudioCaptureClient capture, AudioFormat format)
    {
        var fill = Source.Kind switch
        {
            AudioSourceKind.System => _options.FillSilenceForSystemLoopback,
            AudioSourceKind.Application => _options.FillSilenceForProcessLoopback,
            _ => false,
        };
        var filler = fill ? new SilenceGapFiller(format.SampleRate, StartedAtQpc, _options.SilenceHoldback, _options.GapThreshold) : null;
        var timeoutMs = filler is null ? 200 : 10;
        while (true)
        {
            if (!_bufferEvent.WaitOne(timeoutMs))
            {
                _counters.EventTimeout();
            }

            var hr = Drain(capture, format, filler);
            if (hr < 0)
            {
                ReportLoss(MapLoss(hr), hr);
                return;
            }

            var pending = Volatile.Read(ref _pendingLoss);
            if (pending != NoLoss)
            {
                ReportLoss((CaptureLostReason)pending, 0);
                return;
            }

            if (_stopRequested)
            {
                var statistics = _counters.Snapshot(); // once per capture, at stop
                LogStopped(_logger, Source, statistics);
                return;
            }

            if (filler is not null)
            {
                var n = filler.OnIdle(QpcClock.Now, out var silenceStart);
                if (n > 0)
                {
                    _counters.Synthesized(n);
                    Emit(null, 0, n, silenceStart, 0, CapturePacketFlags.Silent | CapturePacketFlags.Synthesized);
                }
            }
        }
    }

    /// <summary>Takes every packet WASAPI holds. Returns a failing HRESULT on device loss or when a packet cannot be released.</summary>
    internal int Drain(CoreAudio.IAudioCaptureClient capture, AudioFormat format, SilenceGapFiller? filler)
    {
        var block = format.BlockAlign;
        var released = CoreAudio.SOk;
        while (true)
        {
            // A packet WASAPI would not take back means the stream is broken: the packet already copied is kept, then
            // capture ends as lost instead of reading a buffer it may not own.
            if (released < 0)
            {
                return released;
            }

            var hr = capture.GetNextPacketSize(out var next);
            if (hr < 0)
            {
                return hr;
            }

            if (next == 0)
            {
                return CoreAudio.SOk;
            }

            hr = capture.GetBuffer(out var data, out var frameCount, out var wasapiFlags, out var devicePosition, out var qpcPosition);
            if (hr == CoreAudio.AudclntSBufferEmpty)
            {
                return CoreAudio.SOk;
            }

            if (hr < 0)
            {
                return hr;
            }

            var frames = (int)frameCount;
            var bytes = frames * block;
            var silent = (wasapiFlags & CoreAudio.BufferFlagsSilent) != 0;
            byte[]? buffer = null;
            if (!silent && frames > 0)
            {
                buffer = ArrayPool<byte>.Shared.Rent(bytes);
                Marshal.Copy(data, buffer, 0, bytes);
            }

            released = capture.ReleaseBuffer(frameCount);
            _counters.Packet(frames, wasapiFlags);
            if (frames == 0)
            {
                continue;
            }

            var flags = (CapturePacketFlags)(wasapiFlags & 0x7);
            var qpc = (long)qpcPosition;
            var reliable = (wasapiFlags & CoreAudio.BufferFlagsTimestampError) == 0 && qpc > 0;
            if (filler is null && !reliable)
            {
                // No trustworthy time: place it right after the previous packet so the time gate stays sane.
                qpc = _lastEndQpc > 0 ? _lastEndQpc : QpcClock.Now;
            }

            if (filler is not null)
            {
                var adjust = filler.OnPacket(qpc, frames, reliable);
                if (adjust.SilenceFrames > 0)
                {
                    _counters.Synthesized(adjust.SilenceFrames);
                    Emit(null, 0, adjust.SilenceFrames, adjust.SilenceStartQpc, 0, CapturePacketFlags.Silent | CapturePacketFlags.Synthesized);
                }

                if (adjust.TrimFrames > 0)
                {
                    _counters.Trimmed(adjust.TrimFrames);
                    if (adjust.TrimFrames >= frames)
                    {
                        if (buffer is not null)
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }

                        continue;
                    }

                    var cut = adjust.TrimFrames * block;
                    if (buffer is not null)
                    {
                        Buffer.BlockCopy(buffer, cut, buffer, 0, bytes - cut);
                    }

                    frames -= adjust.TrimFrames;
                    bytes -= cut;
                    devicePosition += (ulong)adjust.TrimFrames;
                }

                if (!reliable)
                {
                    flags |= CapturePacketFlags.TimestampError;
                }

                qpc = adjust.KeptStartQpc;
            }

            Emit(buffer, bytes, frames, qpc, devicePosition, flags);
            _lastEndQpc = qpc + QpcClock.FramesToTicks(frames, format.SampleRate);
        }
    }

    private void Emit(byte[]? buffer, int bytes, int frames, long qpc, ulong devicePosition, CapturePacketFlags flags)
    {
        var dropped = _pendingDropped;
        if (dropped > 0)
        {
            flags |= CapturePacketFlags.AfterOverrun;
        }

        var packet = new CapturePacket(buffer, bytes, frames, qpc, devicePosition, buffer is null ? flags | CapturePacketFlags.Silent : flags, dropped);
        if (_channel.Writer.TryWrite(packet))
        {
            _pendingDropped = 0;
            return;
        }

        // The consumer is behind: never wait. Count it and let the next packet carry the hole so the writer covers it.
        _counters.Overrun(frames);
        _pendingDropped += frames;
        packet.Release();
    }

    private (CoreAudio.IAudioClient Client, AudioFormat Format, TimeSpan Buffer) ActivateEndpoint()
    {
        var isLoopback = Source.Kind == AudioSourceKind.System;
        var what = isLoopback ? "output device" : "microphone";
        var enumerator = CoreAudio.CreateDeviceEnumerator();
        CoreAudio.IMMDevice? device = null;
        try
        {
            var hr = enumerator.GetDevice(Source.EndpointId!, out device);
            if (hr < 0)
            {
                throw new AudioSourceUnavailableException(Source, $"The {what} is no longer connected (it may have been unplugged). Reconnect it or choose another source.", hr);
            }

            device.GetState(out var state);
            if (state != CoreAudio.DeviceStateActive)
            {
                throw new AudioSourceUnavailableException(Source, $"The {what} is disabled or unplugged. Reconnect or enable it in Windows Sound settings, or choose another source.");
            }

            var iid = CoreAudio.IidAudioClient;
            Check(device.Activate(ref iid, CoreAudio.ClsctxAll, IntPtr.Zero, out var instance), "IMMDevice::Activate");
            var client = (CoreAudio.IAudioClient)instance;
            var mix = IntPtr.Zero;
            try
            {
                // Inside the try: a failing GetMixFormat must release the client too.
                Check(client.GetMixFormat(out mix), "GetMixFormat");
                var format = NativeWaveFormat.Read(mix);
                var flags = CoreAudio.StreamFlagsEventCallback | (isLoopback ? CoreAudio.StreamFlagsLoopback : 0);
                Check(client.Initialize(CoreAudio.AudclntShareModeShared, flags, _options.EndpointBufferDuration.Ticks, 0, mix, IntPtr.Zero), "IAudioClient::Initialize");
                return (client, format, _options.EndpointBufferDuration);
            }
            catch
            {
                Marshal.ReleaseComObject(client);
                throw;
            }
            finally
            {
                if (mix != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(mix);
                }
            }
        }
        finally
        {
            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            Marshal.ReleaseComObject(enumerator);
        }
    }

    private (CoreAudio.IAudioClient Client, AudioFormat Format, TimeSpan Buffer) ActivateProcessLoopback()
    {
        var pid = Source.ProcessId;
        try
        {
            _watchedProcess = Process.GetProcessById(pid);
        }
        catch (ArgumentException ex)
        {
            throw new AudioSourceUnavailableException(Source, $"The app (process {pid}) is no longer running. Start it again or choose another source.", 0, ex);
        }

        WatchProcessExit();
        var activation = new CoreAudio.AudioClientActivationParams
        {
            ActivationType = CoreAudio.ActivationTypeProcessLoopback,
            TargetProcessId = (uint)pid,
            ProcessLoopbackMode = CoreAudio.ProcessLoopbackModeIncludeTargetProcessTree,
        };
        var pParams = Marshal.AllocHGlobal(Marshal.SizeOf<CoreAudio.AudioClientActivationParams>());
        var pVariant = Marshal.AllocHGlobal(Marshal.SizeOf<CoreAudio.PropVariantBlob>());
        try
        {
            Marshal.StructureToPtr(activation, pParams, false);
            Marshal.StructureToPtr(
                new CoreAudio.PropVariantBlob { Vt = CoreAudio.VtBlob, Size = (uint)Marshal.SizeOf<CoreAudio.AudioClientActivationParams>(), Data = pParams },
                pVariant,
                false);
            var handler = new ActivationCompletionHandler();
            var iid = CoreAudio.IidAudioClient;
            int hr;
            try
            {
                hr = NativeMethods.ActivateAudioInterfaceAsync(CoreAudio.ProcessLoopbackDevicePath, ref iid, pVariant, handler, out var operation);

                // Waiting here blocks only this capture thread; the opener awaits asynchronously.
                if (hr >= 0 && Task.WaitAny([handler.Completion], _options.ActivationTimeout) != 0)
                {
                    // Windows may still finish later: the client it hands over then is released, not leaked.
                    ReleaseWhenCompleted(handler.Completion, static client => Marshal.ReleaseComObject(client));
                    throw new AudioSourceUnavailableException(Source, $"Windows did not open per-app capture for process {pid} within {_options.ActivationTimeout.TotalSeconds:0} s. Try again, or record everything this PC plays instead.");
                }

                GC.KeepAlive(operation);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new AudioSourceUnavailableException(Source, "Per-app capture needs Windows 10 version 2004 or later. Record everything this PC plays instead.", 0, ex);
            }

            if (hr < 0)
            {
                throw new AudioSourceUnavailableException(Source, $"Per-app capture could not start for process {pid} (0x{hr:X8}). Record everything this PC plays instead.", hr);
            }

            CoreAudio.IAudioClient client;
            try
            {
                client = handler.Completion.GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not AudioSourceUnavailableException)
            {
                throw new AudioSourceUnavailableException(Source, $"Per-app capture could not start for process {pid} (0x{ex.HResult:X8}). Record everything this PC plays instead.", ex.HResult, ex);
            }

            var format = _options.ProcessLoopbackFormat;
            var pFormat = NativeWaveFormat.Allocate(format);
            try
            {
                // GetMixFormat/GetDevicePeriod are E_NOTIMPL here; the caller picks the format and the engine converts.
                Check(
                    client.Initialize(
                        CoreAudio.AudclntShareModeShared,
                        CoreAudio.StreamFlagsLoopback | CoreAudio.StreamFlagsEventCallback | CoreAudio.StreamFlagsAutoConvertPcm | CoreAudio.StreamFlagsSrcDefaultQuality,
                        _options.ProcessLoopbackBufferDuration.Ticks,
                        0,
                        pFormat,
                        IntPtr.Zero),
                    "IAudioClient::Initialize (process loopback)");
            }
            catch
            {
                Marshal.ReleaseComObject(client);
                throw;
            }
            finally
            {
                Marshal.FreeHGlobal(pFormat);
            }

            return (client, format, _options.ProcessLoopbackBufferDuration);
        }
        finally
        {
            Marshal.FreeHGlobal(pParams);
            Marshal.FreeHGlobal(pVariant);
        }
    }

    private void WatchProcessExit()
    {
        try
        {
            _watchedProcess!.EnableRaisingEvents = true;
            _watchedProcess.Exited += (_, _) => SignalLoss(CaptureLostReason.ProcessExited);
            if (_watchedProcess.HasExited)
            {
                SignalLoss(CaptureLostReason.ProcessExited);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Elevated or protected process: no exit notification; capture still works and ends on device loss.
            LogNoExitWatch(_logger, Source, ex.Message);
        }
    }

    private (CoreAudio.IAudioSessionControl? Session, SessionEventsSink? Sink) TryRegisterSessionEvents(CoreAudio.IAudioClient client)
    {
        var iid = CoreAudio.IidAudioSessionControl;
        if (client.GetService(ref iid, out var service) < 0 || service is not CoreAudio.IAudioSessionControl session)
        {
            return (null, null); // Process loopback does not offer session control.
        }

        var sink = new SessionEventsSink(reason => SignalLoss(reason switch
        {
            0 => CaptureLostReason.DeviceInvalidated, // DisconnectReasonDeviceRemoval
            1 => CaptureLostReason.ServiceStopped,    // DisconnectReasonServerShutdown
            _ => CaptureLostReason.SessionDisconnected,
        }));
        if (session.RegisterAudioSessionNotification(sink) < 0)
        {
            Marshal.ReleaseComObject(session);
            return (null, null);
        }

        return (session, sink);
    }

    private void SignalLoss(CaptureLostReason reason)
    {
        Interlocked.CompareExchange(ref _pendingLoss, (int)reason, NoLoss);
        try
        {
            _bufferEvent.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReportLoss(CaptureLostReason reason, int hr)
    {
        var args = new CaptureLostEventArgs(Source, reason, hr, QpcClock.Now);
        Volatile.Write(ref _loss, args);
        LogLost(_logger, Source, reason, hr);
        ThreadPool.UnsafeQueueUserWorkItem(static state => state.Self.Lost?.Invoke(state.Self, state.Args), (Self: this, Args: args), preferLocal: false);
    }

    private IntPtr RegisterMmcss()
    {
        foreach (var task in _options.MmcssTasks)
        {
            uint index = 0;
            var handle = NativeMethods.AvSetMmThreadCharacteristics(task, ref index);
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        var mmcssError = Marshal.GetLastPInvokeError();
        LogNoMmcss(_logger, Source, mmcssError);
        return IntPtr.Zero;
    }

    private void Cleanup(CoreAudio.IAudioClient? client, CoreAudio.IAudioCaptureClient? capture, CoreAudio.IAudioSessionControl? session, SessionEventsSink? sink, IntPtr mmcss)
    {
        if (session is not null)
        {
            if (sink is not null)
            {
                session.UnregisterAudioSessionNotification(sink);
            }

            Marshal.ReleaseComObject(session);
        }

        if (capture is not null)
        {
            Marshal.ReleaseComObject(capture);
        }

        if (client is not null)
        {
            client.Stop();
            Marshal.ReleaseComObject(client);
        }

        if (mmcss != IntPtr.Zero)
        {
            NativeMethods.AvRevertMmThreadCharacteristics(mmcss);
        }

        if (_watchedProcess is not null)
        {
            try
            {
                _watchedProcess.EnableRaisingEvents = false;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
            }
        }
    }

    private AudioSourceUnavailableException Unavailable(Exception ex)
    {
        var what = Source.Kind switch
        {
            AudioSourceKind.Microphone => "the microphone",
            AudioSourceKind.System => "the output device",
            _ => $"the app (process {Source.ProcessId})",
        };
        var detail = ex.HResult == CoreAudio.EAccessDenied
            ? "Windows denied access. Allow microphone access for desktop apps in Settings › Privacy & security › Microphone."
            : $"Windows reported 0x{ex.HResult:X8}. Reconnect the device or choose another source.";
        return new AudioSourceUnavailableException(Source, $"Could not open {what} for recording. {detail}", ex.HResult, ex);
    }

    /// <summary>
    /// When an activation nobody waits for any more completes, hands its result to <paramref name="release"/>; a failed
    /// activation's exception is observed so it never surfaces as an unobserved task exception.
    /// </summary>
    internal static void ReleaseWhenCompleted<T>(Task<T> activation, Action<T> release)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(release);
        activation.ContinueWith(
            static (task, state) =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    ((Action<T>)state!)(task.Result);
                }
                else
                {
                    _ = task.Exception;
                }
            },
            release,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static CaptureLostReason MapLoss(int hr) => hr switch
    {
        CoreAudio.AudclntEDeviceInvalidated or CoreAudio.AudclntEResourcesInvalidated => CaptureLostReason.DeviceInvalidated,
        CoreAudio.AudclntEServiceNotRunning => CaptureLostReason.ServiceStopped,
        _ => CaptureLostReason.Error,
    };

    private static void Check(int hr, string call)
    {
        if (hr < 0)
        {
            throw new CoreAudioCallException(call, hr);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Capture {Source} opened: {Format}, buffer {BufferFrames} frames ({BufferMs} ms), activation {ActivationMs} ms")]
    private static partial void LogOpened(ILogger logger, AudioSourceId source, AudioFormat format, uint bufferFrames, double bufferMs, long activationMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Capture {Source} stopped: {Statistics}")]
    private static partial void LogStopped(ILogger logger, AudioSourceId source, CaptureStatistics statistics);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Capture {Source} lost: {Reason} (0x{HResult:X8})")]
    private static partial void LogLost(ILogger logger, AudioSourceId source, CaptureLostReason reason, int hResult);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Capture {Source}: MMCSS registration failed (error {Error}); running at normal real-time priority")]
    private static partial void LogNoMmcss(ILogger logger, AudioSourceId source, int error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Capture {Source}: no exit notification for the target process ({Reason})")]
    private static partial void LogNoExitWatch(ILogger logger, AudioSourceId source, string reason);
}
