namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>storage.reclaim</c>. <see cref="RecordingIds"/> <c>null</c> means every recording older than
/// Settings › Storage › <c>reclaimOlderThanDays</c>.
/// </summary>
public sealed record StorageReclaimParams
{
    public IReadOnlyList<string>? RecordingIds { get; init; }

    public bool DownmixMono { get; init; }

    /// <summary><c>aac</c> or <c>mp3</c>.</summary>
    public required string Codec { get; init; }

    public int? BitrateKbps { get; init; }
}
