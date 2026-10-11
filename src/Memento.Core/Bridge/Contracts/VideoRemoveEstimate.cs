namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>video.removeEstimate</c>: what Remove video would free ("Would free 11.3 GB now, from 3 recordings").</summary>
public sealed record VideoRemoveEstimate(int Recordings, long Bytes);
