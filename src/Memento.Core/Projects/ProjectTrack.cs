using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Projects;

/// <summary>One track in <c>project.json</c>.</summary>
public sealed record ProjectTrack
{
    /// <summary>Unique in the project: <c>mic</c>, <c>system</c>, <c>app-zoom</c>, <c>mic-2</c>.</summary>
    public required string Id { get; init; }

    public required string SourceId { get; init; }

    /// <summary><c>microphone</c>, <c>system</c> or <c>application</c>.</summary>
    public required string SourceKind { get; init; }

    public required string Name { get; init; }

    /// <summary>Relative to the project folder: <c>tracks/mic.wav</c> while recording, the encoded file after finalize.</summary>
    public required string File { get; init; }

    /// <summary>The capture file (<c>tracks/mic.wav</c>); kept so recovery can find it if finalize was interrupted.</summary>
    public string? CaptureFile { get; init; }

    public int SampleRate { get; init; }

    public int Channels { get; init; }

    /// <summary>Capture bit depth.</summary>
    public int BitsPerSample { get; init; }

    /// <summary><c>pcm</c> or <c>float</c> (capture encoding).</summary>
    public string SampleEncoding { get; init; } = "pcm";

    /// <summary>Codec of <see cref="File"/>: <c>wav</c> until finalize, then the storage codec.</summary>
    public string Codec { get; init; } = "wav";

    /// <summary>Where the track starts on the session timeline (a source turned on mid-recording starts later).</summary>
    public long StartOffsetMs { get; init; }

    public long DurationMs { get; init; }

    public string? Sha256 { get; init; }

    public long? EndedEarlyAtMs { get; init; }

    /// <summary><c>disabled</c>, <c>sourceLost</c>, <c>diskFull</c> or <c>error</c> when the track ended early.</summary>
    public string? EndReason { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
