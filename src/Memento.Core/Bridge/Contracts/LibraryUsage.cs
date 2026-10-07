namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>library.usage</c> (Settings › Storage and history › Usage).</summary>
/// <param name="TotalBytes">Everything in the library folder.</param>
/// <param name="FreeBytes">Free space on the library drive (0 when it cannot be read).</param>
/// <param name="Count">Recordings in the library.</param>
public sealed record LibraryUsage(long TotalBytes, long FreeBytes, int Count, LibraryUsageLargest? Largest);
