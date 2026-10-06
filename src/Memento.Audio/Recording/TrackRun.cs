using Memento.Audio.Capture;
using Memento.Audio.Writing;

namespace Memento.Audio.Recording;

/// <summary>One capture + writer pair inside a session, and the consumer pump between them.</summary>
internal sealed class TrackRun(AudioSourceId id, string name, string fileStem, IAudioCapture capture, TrackWriter writer)
{
    public AudioSourceId Id { get; } = id;

    public string SourceId { get; } = id.ToString();

    public string Name { get; } = name;

    public string FileStem { get; } = fileStem;

    public IAudioCapture Capture { get; } = capture;

    public TrackWriter Writer { get; } = writer;

    public LevelMeter Meter { get; } = new();

    public DriftMeter Drift { get; } = new(capture.Format.SampleRate);

    /// <summary>Completes when the pump has drained the channel, the writer is closed and the capture disposed.</summary>
    public Task Completion { get; set; } = Task.CompletedTask;

    /// <summary>Set by the session before it stops or disables this track (so a late loss is not reported).</summary>
    public volatile bool Ending;

    private long _hold = long.MaxValue;

    public TrackEndReason? PendingEndReason { get; set; }

    public long? PendingEndQpc { get; set; }

    public TrackEndReason? EndReason { get; set; }

    public long? EndedAtQpc { get; set; }

    public string? Error { get; set; }

    public IOException? WriteFailure { get; private set; }

    public long WriteFailedAtQpc { get; private set; }

    public TrackResult? Result { get; set; }

    public bool IsActive => EndReason is null && !Ending;

    /// <summary>
    /// Moves packets from the capture channel into the writer and meters until the channel completes.
    /// <para>
    /// A packet is written only once the clock has passed its last frame. Loopback stamps packets with their
    /// presentation time, 10–20 ms <em>ahead</em> of when they arrive; written on arrival, frames after a pause or
    /// stop instant would already be on disk before the session asks to cut there. Holding them until they are due
    /// lets the time gate cut every track at the same instant. Capture endpoints are always due on arrival.
    /// </para>
    /// </summary>
    public async Task PumpAsync(Action<TrackRun, IOException> onWriteFailed)
    {
        var reader = Capture.Packets;
        var held = new Queue<CapturePacket>();
        Task<bool>? waiting = null;
        while (true)
        {
            while (held.Count > 0 && IsDue(held.Peek()))
            {
                Write(held.Dequeue(), onWriteFailed);
            }

            waiting ??= reader.WaitToReadAsync().AsTask();
            if (held.Count > 0 && !waiting.IsCompleted)
            {
                var dueIn = TimeSpan.FromTicks(Math.Clamp(EndQpc(held.Peek()) - QpcClock.Now, QpcClock.TicksPerMillisecond, 50 * QpcClock.TicksPerMillisecond));
                await Task.WhenAny(waiting, Task.Delay(dueIn)).ConfigureAwait(false);
            }
            else
            {
                await waiting.ConfigureAwait(false);
            }

            if (waiting.IsCompleted)
            {
                var more = await waiting.ConfigureAwait(false);
                waiting = null;
                if (!more)
                {
                    break;
                }

                while (reader.TryRead(out var packet))
                {
                    held.Enqueue(packet);
                }
            }
        }

        // The channel is complete: everything left goes through the time gate, which drops what is past the end.
        while (held.Count > 0)
        {
            Write(held.Dequeue(), onWriteFailed);
        }
    }

    /// <summary>
    /// Holds back every packet ending after <paramref name="qpc"/> until <see cref="ReleaseHold"/>. The session sets
    /// this before it computes a pause/resume/stop instant and applies it to the writers, so no packet past that
    /// instant can slip into a writer in between (a writer lock can be busy with a flush or checkpoint).
    /// </summary>
    public void Hold(long qpc) => Volatile.Write(ref _hold, qpc);

    public void ReleaseHold() => Volatile.Write(ref _hold, long.MaxValue);

    private bool IsDue(CapturePacket packet)
    {
        // Read the clock before the hold: if no hold is visible yet, the session's instant comes after this reading.
        var now = QpcClock.Now;
        var hold = Volatile.Read(ref _hold);
        return EndQpc(packet) <= Math.Min(now, hold);
    }

    private long EndQpc(CapturePacket packet) => packet.QpcPosition + QpcClock.FramesToTicks(packet.Frames, Capture.Format.SampleRate);

    private void Write(CapturePacket packet, Action<TrackRun, IOException> onWriteFailed)
    {
        var format = Capture.Format;
        try
        {
            if (WriteFailure is not null)
            {
                return; // Keep draining so the capture thread never fills up; nothing more is written.
            }

            if (packet.DroppedFramesBefore > 0)
            {
                var dropped = packet.DroppedFramesBefore;
                var start = packet.QpcPosition - QpcClock.FramesToTicks(dropped, format.SampleRate);
                for (long done = 0; done < dropped;)
                {
                    var n = (int)Math.Min(format.SampleRate, dropped - done);
                    Writer.WriteSilence(n, start + QpcClock.FramesToTicks(done, format.SampleRate));
                    done += n;
                }

                Meter.AddSilence((int)Math.Min(int.MaxValue, dropped), format.Channels);
            }

            if (packet.IsSilent)
            {
                Writer.WriteSilence(packet.Frames, packet.QpcPosition);
                Meter.AddSilence(packet.Frames, format.Channels);
            }
            else
            {
                Writer.Write(packet.Data, packet.QpcPosition);
                Meter.Add(packet.Data, format);
            }

            Drift.Add(packet.QpcPosition, packet.Frames, packet.HasReliableTimestamp);
            Writer.FlushIfDue();
        }
        catch (IOException ex)
        {
            WriteFailure = ex;
            WriteFailedAtQpc = packet.QpcPosition;
            onWriteFailed(this, ex);
        }
        finally
        {
            packet.Release();
        }
    }

    /// <summary>Closes the writer (final checkpoint). A failing disk must not stop the rest of the shutdown.</summary>
    public void CloseWriter()
    {
        try
        {
            Writer.Dispose();
        }
        catch (IOException ex)
        {
            Error ??= ex.Message;
        }
    }

    public TrackStatus Status(SessionTimeline timeline)
    {
        var first = Writer.FirstFrameQpc;
        return new TrackStatus(
            SourceId,
            Id.Kind,
            Name,
            FileStem,
            Writer.Parts,
            Writer.StorageFormat.SampleRate,
            Writer.StorageFormat.Channels,
            Writer.Duration,
            first is { } f ? timeline.ToTimeline(f) : TimeSpan.Zero,
            EndReason is null or TrackEndReason.SessionStopped || EndedAtQpc is null ? null : timeline.ToTimeline(EndedAtQpc.Value),
            EndReason);
    }

    public TrackResult BuildResult(SessionTimeline timeline)
    {
        var status = Status(timeline);
        return new TrackResult(
            SourceId,
            Id.Kind,
            Name,
            FileStem,
            Writer.Parts,
            Capture.Format,
            Writer.StorageFormat,
            Writer.FramesWritten,
            Writer.Duration,
            status.StartOffset,
            status.EndedEarlyAt,
            EndReason ?? TrackEndReason.SessionStopped,
            Writer.Gaps,
            Drift.Ppm,
            Capture.Statistics,
            Error);
    }
}
