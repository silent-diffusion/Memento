using Memento.Video.Encoding;
using Memento.Video.Sources;

namespace Memento.Video.Tests.Fakes;

/// <summary>Records the time stamps it is given; can be told to refuse (no free surface) or to fail.</summary>
internal sealed class FakeTrackWriter(string path, VideoEncoderSettings settings, H264EncoderInfo encoder) : IVideoTrackWriter
{
    public string FilePath { get; } = path;

    public VideoEncoderSettings Settings { get; } = settings;

    public H264EncoderInfo Encoder { get; } = encoder;

    public List<long> Timestamps { get; } = [];

    public int Checkpoints { get; private set; }

    public bool Finalized { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>The next write's result instead of Written (consumed by one write).</summary>
    public VideoWriteResult? Next { get; set; }

    public VideoCaptureException? Failure { get; set; }

    public VideoWriterStatistics Statistics => new(Timestamps.Count, Timestamps.Count, Timestamps.Count * 1000L, 0, 0, Failure);

    public VideoWriteResult WriteFrame(in VideoFrame frame, long timestampTicks)
    {
        if (Next is { } next)
        {
            Next = null;
            return next;
        }

        Timestamps.Add(timestampTicks);
        return VideoWriteResult.Written;
    }

    public void Checkpoint() => Checkpoints++;

    public Task FinalizeAsync(CancellationToken cancellationToken)
    {
        Finalized = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
