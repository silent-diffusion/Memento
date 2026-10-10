namespace Memento.Video.Encoding;

/// <summary>What a writer has done so far.</summary>
/// <param name="FramesSubmitted">Frames handed to the encoder.</param>
/// <param name="FramesEncoded">Frames the encoder has finished (Media Foundation's sink writer statistics).</param>
/// <param name="BytesWritten">Size of the file on disk.</param>
/// <param name="AverageLatencyMs">Average time from submitting a frame to the encoder finishing it.</param>
/// <param name="MaxLatencyMs">Longest such time.</param>
/// <param name="Failure">Set when the writer failed.</param>
public sealed record VideoWriterStatistics(
    long FramesSubmitted,
    long FramesEncoded,
    long BytesWritten,
    double AverageLatencyMs,
    double MaxLatencyMs,
    VideoCaptureException? Failure)
{
    public static VideoWriterStatistics Empty { get; } = new(0, 0, 0, 0, 0, null);
}
