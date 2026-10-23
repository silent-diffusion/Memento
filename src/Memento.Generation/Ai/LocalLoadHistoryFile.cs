using Memento.AI.Local;

namespace Memento.Generation.Ai;

/// <summary><c>local-model-loads.json</c>: the last measured load of each larger local model.</summary>
public sealed record LocalLoadHistoryFile(int SchemaVersion, IReadOnlyList<LocalLoadMeasurement> Loads)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    public IReadOnlyList<LocalLoadMeasurement> Loads { get; set; } = Loads ?? [];

    public const int CurrentSchemaVersion = 1;
}
