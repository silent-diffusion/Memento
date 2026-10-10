namespace Memento.Core.Audio;

/// <summary>One window of the live transcript (<see cref="LiveWindows"/>).</summary>
/// <param name="Skipped">Windows passed over to catch up (0 when heard in order).</param>
public sealed record LiveWindow(int Index, long StartMs, long EndMs, int Skipped);
