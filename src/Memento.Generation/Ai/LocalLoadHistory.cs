using System.Text.Json;
using Memento.AI.Local;
using Memento.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Memento.Generation.Ai;

/// <summary>
/// The last measured load of each larger local model (<see cref="LocalLoadMeasurement"/>), so the next
/// <see cref="MemoryPlanner"/> plan is checked and corrected against what the worker really took and how fast it wrote
/// (ARCHITECTURE.md §8). Kept in <c>local-model-loads.json</c> (schema v1) beside the settings; written atomically; a
/// damaged or newer file reads as empty and is replaced on the next load. Holds sizes and speeds only.
/// </summary>
public sealed partial class LocalLoadHistory(string path, ILogger<LocalLoadHistory> logger)
{
    public const string FileName = "local-model-loads.json";

    private readonly object _gate = new();
    private readonly ILogger<LocalLoadHistory> _logger = logger;
    private Dictionary<string, LocalLoadMeasurement>? _loads;

    /// <summary>The last load of <paramref name="modelId"/>, or <c>null</c>.</summary>
    public LocalLoadMeasurement? Last(string modelId)
    {
        lock (_gate)
        {
            return Read().GetValueOrDefault(modelId);
        }
    }

    /// <summary>Keeps <paramref name="measurement"/> as the model's last load.</summary>
    public async Task RecordAsync(LocalLoadMeasurement measurement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        LocalLoadHistoryFile file;
        lock (_gate)
        {
            var loads = Read();
            loads[measurement.ModelId] = measurement;
            file = new LocalLoadHistoryFile(LocalLoadHistoryFile.CurrentSchemaVersion, loads.Values.OrderBy(l => l.ModelId, StringComparer.Ordinal).ToList());
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(file, LocalLoadHistoryJsonContext.Default.LocalLoadHistoryFile), cancellationToken);
            await AtomicReplace.ReplaceAsync(temporary, path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A measurement is a refinement: without it the next plan uses the estimates.
            LogWriteFailed(ex, path);
        }
    }

    private Dictionary<string, LocalLoadMeasurement> Read()
    {
        if (_loads is not null)
        {
            return _loads;
        }

        _loads = new Dictionary<string, LocalLoadMeasurement>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize(File.ReadAllText(path), LocalLoadHistoryJsonContext.Default.LocalLoadHistoryFile) is { SchemaVersion: LocalLoadHistoryFile.CurrentSchemaVersion } file)
            {
                foreach (var load in file.Loads.Where(l => l is not null && !string.IsNullOrWhiteSpace(l.ModelId)))
                {
                    _loads[load.ModelId] = load;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogReadFailed(ex, path);
        }

        return _loads;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The local model load history {Path} could not be read; the next plan uses the estimates")]
    private partial void LogReadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The local model load history {Path} could not be written")]
    private partial void LogWriteFailed(Exception exception, string path);
}
