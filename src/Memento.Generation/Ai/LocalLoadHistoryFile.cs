using Memento.AI.Local;

namespace Memento.Generation.Ai;

/// <summary><c>local-model-loads.json</c>: the last measured load of each larger local model.</summary>
public sealed record LocalLoadHistoryFile(int SchemaVersion, IReadOnlyList<LocalLoadMeasurement> Loads)
{
    public const int CurrentSchemaVersion = 1;
}
