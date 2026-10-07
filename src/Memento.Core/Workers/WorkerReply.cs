namespace Memento.Core.Workers;

/// <summary>
/// A line from the worker to the host. <c>ready</c> (pid), <c>device</c>, <c>track</c>, <c>progress</c> (percent and,
/// for transcription, the segments finished in that window), then exactly one of <c>result</c>, <c>error</c> (code and
/// message) or <c>cancelled</c>; <c>log</c> lines may appear anywhere.
/// </summary>
public sealed record WorkerReply
{
    public required string Type { get; init; }

    public int? Pid { get; init; }

    public double? Percent { get; init; }

    public string? TrackId { get; init; }

    /// <summary>Windows of <see cref="TrackId"/> finished so far (the resume point).</summary>
    public int? WindowsDone { get; init; }

    public IReadOnlyList<WorkerSegment>? Segments { get; init; }

    public WorkerTrackInfo? Track { get; init; }

    public WorkerDevice? Device { get; init; }

    public string? Language { get; init; }

    public TranscribeResult? Transcription { get; init; }

    public DiarizeResult? Diarization { get; init; }

    public string? Code { get; init; }

    public string? Message { get; init; }

    public string? Level { get; init; }
}
