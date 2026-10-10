namespace Memento.Video.Encoding;

public enum VideoWriteResult
{
    Written,

    /// <summary>The encoder is behind and every surface is in use; the frame is dropped, nothing waits.</summary>
    NoFreeSurface,

    /// <summary>The encoder or the file failed; see <see cref="VideoWriterStatistics.Failure"/>.</summary>
    Failed,
}
