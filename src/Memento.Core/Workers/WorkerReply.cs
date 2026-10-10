using System.Text.Json;

namespace Memento.Core.Workers;

/// <summary>
/// A line from the worker to the host. <c>ready</c> (pid), <c>device</c>, <c>track</c>, <c>progress</c> (percent and,
/// for transcription, the segments finished in that window), <c>diarized</c> (a speaker job's finished track), then
/// exactly one of <c>result</c>, <c>error</c> (code and message) or <c>cancelled</c>; <c>log</c> lines may appear anywhere.
/// </summary>
public sealed record WorkerReply
{
    public required string Type { get; init; }

    public int? Pid { get; init; }

    public double? Percent { get; init; }

    public string? TrackId { get; init; }

    /// <summary>Windows of <see cref="TrackId"/> finished so far (the resume point).</summary>
    public int? WindowsDone { get; init; }

    /// <summary>2.0: a <c>heard</c> line's window (<see cref="LiveAudio.Window"/>).</summary>
    public int? Window { get; init; }

    public IReadOnlyList<WorkerSegment>? Segments { get; init; }

    public WorkerTrackInfo? Track { get; init; }

    public WorkerDevice? Device { get; init; }

    public string? Language { get; init; }

    public TranscribeResult? Transcription { get; init; }

    public DiarizeResult? Diarization { get; init; }

    /// <summary>A <c>diarized</c> line: one finished track of a speaker job.</summary>
    public DiarizedTrack? Diarized { get; init; }

    /// <summary>A local model job's <c>result</c> (Memento.AI's <c>LocalLlmResult</c>), raw JSON.</summary>
    public JsonElement? Llm { get; init; }

    /// <summary>A local model job's <c>device</c> line (where the model was loaded), raw JSON.</summary>
    public JsonElement? LlmDevice { get; init; }

    /// <summary>A local model job's progress details, raw JSON. Its text delta is content: shown, never logged.</summary>
    public JsonElement? LlmProgress { get; init; }

    public string? Code { get; init; }

    public string? Message { get; init; }

    public string? Level { get; init; }
}
