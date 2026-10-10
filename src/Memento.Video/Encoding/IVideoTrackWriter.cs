using Memento.Video.Sources;

namespace Memento.Video.Encoding;

/// <summary>
/// Encodes pictures into one video file. Called from one thread (the session's pacing thread). It never blocks for
/// long: when the encoder has no free surface the frame is refused (<see cref="VideoWriteResult.NoFreeSurface"/>)
/// and the session counts it as dropped.
/// </summary>
public interface IVideoTrackWriter : IAsyncDisposable
{
    /// <summary>The file being written.</summary>
    string FilePath { get; }

    VideoEncoderSettings Settings { get; }

    /// <summary>The encoder in use.</summary>
    H264EncoderInfo Encoder { get; }

    VideoWriterStatistics Statistics { get; }

    /// <summary>Encodes <paramref name="frame"/> at <paramref name="timestampTicks"/> (100 ns from the track's start).</summary>
    VideoWriteResult WriteFrame(in VideoFrame frame, long timestampTicks);

    /// <summary>Forces what the encoder has written so far to disk (<c>FlushFileBuffers</c>). Cheap; called at every recording checkpoint.</summary>
    void Checkpoint();

    /// <summary>Drains the encoder and closes the file.</summary>
    Task FinalizeAsync(CancellationToken cancellationToken);
}
