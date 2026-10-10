namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>library.usage</c> (Settings › Storage and history › Usage).</summary>
/// <param name="TotalBytes">Everything in the library folder.</param>
/// <param name="FreeBytes">Free space on the library drive (0 when it cannot be read).</param>
/// <param name="Count">Recordings in the library.</param>
/// <param name="SeparateTracksBytes">2.0: the separate track files of stored recordings that also have a mix, which "Keep only the mix" would free.</param>
/// <param name="SeparateTracksRecordings">2.0: how many recordings those are.</param>
/// <param name="MixOnlyRecordings">2.0: recordings that already keep only their mix.</param>
public sealed record LibraryUsage(
    long TotalBytes,
    long FreeBytes,
    int Count,
    LibraryUsageLargest? Largest,
    long SeparateTracksBytes = 0,
    int SeparateTracksRecordings = 0,
    int MixOnlyRecordings = 0);
