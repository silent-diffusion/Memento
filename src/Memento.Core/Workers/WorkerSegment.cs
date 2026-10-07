using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Workers;

/// <summary>A finished transcript segment from the worker. Times are seconds on the recording timeline.</summary>
public sealed record WorkerSegment(double Start, double End, string Text, double Confidence, IReadOnlyList<TranscriptWord> Words);
