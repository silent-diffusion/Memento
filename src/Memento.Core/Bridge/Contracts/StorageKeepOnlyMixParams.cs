namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>storage.keepOnlyMix</c> (2.0). <see cref="RecordingIds"/> <c>null</c> means every stored recording
/// that still has separate track files.
/// </summary>
public sealed record StorageKeepOnlyMixParams
{
    public IReadOnlyList<string>? RecordingIds { get; init; }
}
