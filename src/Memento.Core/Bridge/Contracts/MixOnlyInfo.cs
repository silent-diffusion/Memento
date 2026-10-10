namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// <c>Project.mixOnly</c> (2.0): "Keep only the mix" removed this recording's separate track files, so Review and the
/// Export dialog leave out what needs them (per-track export, identifying speakers again by listening).
/// </summary>
/// <param name="Tracks">How many track files were removed.</param>
/// <param name="BytesFreed">Their size.</param>
public sealed record MixOnlyInfo(DateTimeOffset At, int Tracks, long BytesFreed);
