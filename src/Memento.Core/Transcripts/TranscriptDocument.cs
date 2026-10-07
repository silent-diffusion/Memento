using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary><c>transcript.json</c>, schema v1 (ARCHITECTURE.md §7, BRIDGE.md M2). Unknown fields are kept.</summary>
public sealed record TranscriptDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Language { get; init; } = "en";

    public bool LanguageDetected { get; init; }

    public TranscriptEngineInfo Engine { get; init; } = new("whisper.cpp", string.Empty, string.Empty, string.Empty, 0);

    public IReadOnlyList<Speaker> Speakers { get; init; } = [];

    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];

    public bool Reviewed { get; init; }

    /// <summary>Increments on every write.</summary>
    public int Version { get; init; }

    public double LowConfidenceThreshold { get; init; } = 0.5;

    public IReadOnlyList<CoverageGap> CoverageGaps { get; init; } = [];

    /// <summary><c>false</c> for the part a failed or cancelled pass produced before it stopped.</summary>
    public bool Complete { get; init; } = true;

    /// <summary>The write that defined this content; becomes the reason of the version it turns into.</summary>
    public TranscriptChange? LastChange { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The bridge shape (<c>transcript.get</c>).</summary>
    public Transcript ToContract() =>
        new(SchemaVersion, Language, LanguageDetected, Engine, Speakers, Segments, Reviewed, Version, LowConfidenceThreshold, CoverageGaps);

    /// <summary>All segment text, for the library's full-text index.</summary>
    public string IndexText() => string.Join(' ', Segments.Select(s => s.Text));

    /// <summary>Names the user gave speakers, for the library's people search.</summary>
    public IReadOnlyList<string> RenamedSpeakers() => Speakers.Where(s => s.Renamed).Select(s => s.Name).ToList();
}
