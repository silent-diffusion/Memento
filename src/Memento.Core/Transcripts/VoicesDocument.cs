using Memento.Core.Workers;

namespace Memento.Core.Transcripts;

/// <summary>
/// <c>voices.json</c>, schema v1: what the last speaker pass heard. <see cref="Tracks"/> are the diarizer's turns and
/// voice embeddings per track, so identifying the speakers again with another count or other names regroups them at once
/// instead of listening to the audio again (while <see cref="Signature"/>, the models and threshold, still matches).
/// <see cref="Clusters"/> are the voices that won lines, each with its embedding and those lines, so Review can tell which
/// speakers sound most alike later (<c>transcript.reduceSpeakers</c>); lines edited, merged or moved since keep their place
/// here, so a speaker's voice is the mix of the voices of the lines it has now. Written by the speakers stage beside
/// <c>transcript.json</c>; it never leaves the PC, is not exported, and goes with the recording when it is deleted.
/// </summary>
/// <param name="EmbeddingModelId">The catalog id of the voice model; voices of another model are not compared.</param>
/// <param name="Signature">Models and clustering threshold of the pass (as in <c>speakers.partial.json</c>).</param>
public sealed record VoicesDocument(int SchemaVersion, string EmbeddingModelId, string Signature, IReadOnlyList<DiarizedTrack> Tracks, IReadOnlyList<VoiceCluster> Clusters)
{
    public const int CurrentSchemaVersion = 1;
}
