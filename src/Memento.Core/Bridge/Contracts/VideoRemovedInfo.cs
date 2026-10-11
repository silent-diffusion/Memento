namespace Memento.Core.Bridge.Contracts;

/// <summary><c>ProjectVideo.removed</c>: when Remove video ran and what it freed.</summary>
public sealed record VideoRemovedInfo(DateTimeOffset At, long BytesFreed);
