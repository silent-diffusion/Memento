using Memento.Core.Workers;

namespace Memento.Core.Transcripts;

/// <summary>
/// <c>speakers.partial.json</c>: the tracks a speaker pass has finished, written as each one finishes. A pass stopped by
/// a busy PC, a crash or closing Memento continues with the other tracks when it runs again with the same models and
/// settings, instead of starting from scratch.
/// </summary>
/// <param name="Signature">Models, expected count and threshold; a pass with another signature starts over.</param>
/// <param name="ElapsedMs">Processing time spent so far across attempts.</param>
public sealed record SpeakersPartial(int SchemaVersion, string Signature, IReadOnlyList<DiarizedTrack> Tracks, long ElapsedMs)
{
    public const int CurrentSchemaVersion = 1;
}
