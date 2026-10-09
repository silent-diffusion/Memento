namespace Memento.Core.Transcripts;

/// <summary>
/// One voice the speakers stage found on one track (a sherpa-onnx cluster), kept in <c>voices.json</c> with the lines it
/// was given, so Review can tell which speakers sound most alike later (<c>transcript.reduceSpeakers</c>).
/// </summary>
/// <param name="Track">The track it was heard on.</param>
/// <param name="Speaker">The cluster number within that track.</param>
/// <param name="Embedding">The voice model's embedding of up to a minute of its turns; empty when there was too little speech.</param>
/// <param name="Seconds">How much speech the embedding was made from.</param>
/// <param name="SegmentIds">The transcript lines this voice won when speakers were identified.</param>
public sealed record VoiceCluster(string Track, int Speaker, IReadOnlyList<float> Embedding, double Seconds, IReadOnlyList<string> SegmentIds);
