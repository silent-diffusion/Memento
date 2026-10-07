namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Storage and history › Reclaim space (M3).</summary>
/// <param name="ReclaimOlderThanDays">Recordings older than this many days are the ones <c>storage.reclaim</c> converts when no ids are given; <c>null</c> for none.</param>
public sealed record LibraryStorageSettingsSnapshot(int? ReclaimOlderThanDays);
