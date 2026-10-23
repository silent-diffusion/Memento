using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary><c>transcript.json</c>, schema v1 (ARCHITECTURE.md §7, BRIDGE.md M2). Unknown fields are kept.</summary>
public sealed record TranscriptDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Language { get; set; } = "en";

    public bool LanguageDetected { get; init; }

    public TranscriptEngineInfo Engine { get; set; } = new("whisper.cpp", string.Empty, string.Empty, string.Empty, 0);

    public IReadOnlyList<Speaker> Speakers { get; set; } = [];

    public IReadOnlyList<TranscriptSegment> Segments { get; set; } = [];

    public bool Reviewed { get; init; }

    /// <summary>Increments on every write.</summary>
    public int Version { get; init; }

    public double LowConfidenceThreshold { get; set; } = 0.5;

    public IReadOnlyList<CoverageGap> CoverageGaps { get; set; } = [];

    /// <summary><c>false</c> for the part a failed or cancelled pass produced before it stopped.</summary>
    public bool Complete { get; set; } = true;

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
