namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>audio.removeEstimate</c> (2.1): what Delete audio would free ("Would free 3.1 GB now, from 12 recordings").</summary>
public sealed record AudioRemoveEstimate(int Recordings, long Bytes);
