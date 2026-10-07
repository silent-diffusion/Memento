namespace Memento.Core.Workers;

/// <summary>A speaker job: sherpa-onnx offline diarization of each track.</summary>
/// <param name="NumClusters">The expected speaker count for a track, or -1 to find it with <see cref="Threshold"/>.</param>
public sealed record DiarizeJob(
    IReadOnlyList<WorkerTrack> Tracks,
    string SegmentationModelPath,
    string EmbeddingModelPath,
    int NumClusters,
    float Threshold,
    int Threads);
