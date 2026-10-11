namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Storage and history › Reclaim space (M3) and Remove video older than (2.0).</summary>
/// <param name="ReclaimOlderThanDays">Recordings older than this many days are the ones <c>storage.reclaim</c> converts when no ids are given; <c>null</c> for none.</param>
/// <param name="RemoveVideoOlderThanDays">2.0: recordings older than this many days lose their video files; <c>null</c> for never.</param>
public sealed record LibraryStorageSettingsSnapshot(int? ReclaimOlderThanDays, int? RemoveVideoOlderThanDays = null);
