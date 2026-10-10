using System.Globalization;
using System.Text.Json;
using Memento.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Settings;

/// <summary>
/// Stores <see cref="AppSettings"/> as JSON. Writes go to <c>settings.json.tmp</c>, are flushed to disk,
/// then moved over the real file (<see cref="AtomicReplace"/>), so a crash mid-write never leaves a half-written settings file.
/// </summary>
public sealed partial class JsonSettingsStore : ISettingsStore, IDisposable
{
    private readonly string _filePath;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _current = new();

    public JsonSettingsStore(string filePath, ILogger<JsonSettingsStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public AppSettings Current => Volatile.Read(ref _current);

    public string FilePath => _filePath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var loaded = await ReadAsync(cancellationToken);
            Volatile.Write(ref _current, loaded);
            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        AppSettings previous;
        AppSettings next;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            previous = Current;
            next = update(previous) with { SchemaVersion = AppSettings.CurrentSchemaVersion };
            Validate(next);
            await WriteAtomicAsync(next, cancellationToken);
            Volatile.Write(ref _current, next);
        }
        finally
        {
            _gate.Release();
        }

        if (next != previous)
        {
            Changed?.Invoke(this, new SettingsChangedEventArgs(previous, next));
        }

        return next;
    }

    public void Dispose() => _gate.Dispose();

    private async Task<AppSettings> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        AppSettings? read;
        try
        {
            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            read = await JsonSerializer.DeserializeAsync(stream, SettingsJsonContext.Default.AppSettings, cancellationToken);
        }
        catch (JsonException ex)
        {
            var setAside = SetAsideUnreadableFile();
            LogUnreadable(_filePath, setAside, ex);
            return new AppSettings();
        }

        return Normalize(read ?? new AppSettings());
    }

    private AppSettings Normalize(AppSettings settings)
    {
        var normalized = settings;
        if (!ThemePreference.IsValid(settings.Theme))
        {
            LogInvalidValue("theme", settings.Theme ?? "null", ThemePreference.System);
            normalized = normalized with { Theme = ThemePreference.System };
        }

        if (!ListDensity.IsValid(settings.ListDensity))
        {
            LogInvalidValue("listDensity", settings.ListDensity ?? "null", ListDensity.Comfortable);
            normalized = normalized with { ListDensity = ListDensity.Comfortable };
        }

        normalized = normalized with
        {
            Recording = NormalizeRecording(settings.Recording),
            Transcription = NormalizeTranscription(settings.Transcription),
            Speakers = NormalizeSpeakers(settings.Speakers),
            History = NormalizeHistory(settings.History),
        };
        return settings.SchemaVersion < 2 ? UpgradeToVersion2(normalized) : normalized;
    }

    /// <summary>
    /// Schema 1 → 2 (2.0): "Keep only the mix" and "During recording" were stored before they did anything (the screens
    /// said so). They now delete separate tracks and run a live transcript, so a value saved back then is not taken as
    /// a choice of either: both start off, and the user turns them on knowing what they do. Written as v2 with the
    /// next change of settings.
    /// </summary>
    private AppSettings UpgradeToVersion2(AppSettings settings)
    {
        var storage = settings.Recording.Storage;
        if (storage.KeepOnlyMix)
        {
            LogNotCarriedOver("recording.storage.keepOnlyMix");
        }

        if (settings.Transcription.Timing == TranscriptionSettings.TimingDuring)
        {
            LogNotCarriedOver("transcription.timing");
        }

        return settings with
        {
            Recording = settings.Recording with { Storage = storage with { KeepOnlyMix = false } },
            Transcription = settings.Transcription with { Timing = TranscriptionSettings.TimingAfter },
        };
    }

    /// <summary>Replaces each out-of-range transcription value with its default, keeping the rest.</summary>
    private TranscriptionSettings NormalizeTranscription(TranscriptionSettings? transcription)
    {
        var defaults = new TranscriptionSettings();
        if (transcription is null)
        {
            return defaults;
        }

        var result = transcription with
        {
            Timing = transcription.Timing ?? defaults.Timing,
            Language = transcription.Language ?? defaults.Language,
            CpuFallbackModelId = transcription.CpuFallbackModelId ?? defaults.CpuFallbackModelId,
        };
        if (!TranscriptionSettings.Timings.Contains(result.Timing, StringComparer.Ordinal))
        {
            LogInvalidValue("transcription.timing", result.Timing, defaults.Timing);
            result = result with { Timing = defaults.Timing };
        }

        if (!TranscriptionSettings.IsValidLanguage(result.Language))
        {
            LogInvalidValue("transcription.language", result.Language, defaults.Language);
            result = result with { Language = defaults.Language };
        }

        if (double.IsNaN(result.LowConfidenceThreshold)
            || result.LowConfidenceThreshold is < TranscriptionSettings.MinLowConfidenceThreshold or > TranscriptionSettings.MaxLowConfidenceThreshold)
        {
            LogInvalidValue("transcription.lowConfidenceThreshold", result.LowConfidenceThreshold.ToString(CultureInfo.InvariantCulture), defaults.LowConfidenceThreshold.ToString(CultureInfo.InvariantCulture));
            result = result with { LowConfidenceThreshold = defaults.LowConfidenceThreshold };
        }

        if (result.Validate() is not null)
        {
            LogInvalidValue("transcription.modelId", result.ModelId ?? "null", "the recommended model");
            result = result with { ModelId = null, CpuFallbackModelId = defaults.CpuFallbackModelId };
        }

        return result;
    }

    private SpeakerSettings NormalizeSpeakers(SpeakerSettings? speakers)
    {
        var defaults = new SpeakerSettings();
        if (speakers is null)
        {
            return defaults;
        }

        var result = speakers with { EmbeddingModelId = speakers.EmbeddingModelId ?? defaults.EmbeddingModelId };
        if (result.Validate() is not null)
        {
            LogInvalidValue("speakers", result.ExpectedSpeakers?.ToString(CultureInfo.InvariantCulture) ?? "auto", "defaults");
            result = result with { ExpectedSpeakers = null, EmbeddingModelId = defaults.EmbeddingModelId };
        }

        return result;
    }

    private HistorySettings NormalizeHistory(HistorySettings? history)
    {
        var defaults = new HistorySettings();
        if (history is null)
        {
            return defaults;
        }

        if (history.Validate() is not null)
        {
            LogInvalidValue("history.keepDays", history.KeepDays.ToString(CultureInfo.InvariantCulture), defaults.KeepDays.ToString(CultureInfo.InvariantCulture));
            return history with { KeepDays = defaults.KeepDays };
        }

        return history;
    }

    /// <summary>Replaces each out-of-range recording value with its default, keeping the rest.</summary>
    private RecordingSettings NormalizeRecording(RecordingSettings? recording)
    {
        var defaults = new RecordingSettings();
        if (recording is null)
        {
            return defaults;
        }

        var result = recording with
        {
            DefaultSourceIds = recording.DefaultSourceIds ?? [],
            Storage = recording.Storage ?? new StorageSettings(),
            KeepSeparateTracks = true,
        };

        if (string.IsNullOrWhiteSpace(result.DefaultType) || result.DefaultType.Length > 64)
        {
            LogInvalidValue("recording.defaultType", result.DefaultType ?? "null", defaults.DefaultType);
            result = result with { DefaultType = defaults.DefaultType };
        }

        if (result.CheckpointSeconds is < RecordingSettings.MinCheckpointSeconds or > RecordingSettings.MaxCheckpointSeconds)
        {
            LogInvalidValue("recording.checkpointSeconds", result.CheckpointSeconds.ToString(CultureInfo.InvariantCulture), defaults.CheckpointSeconds.ToString(CultureInfo.InvariantCulture));
            result = result with { CheckpointSeconds = defaults.CheckpointSeconds };
        }

        if (result.LowSpaceGb is < RecordingSettings.MinLowSpaceGb or > RecordingSettings.MaxLowSpaceGb)
        {
            LogInvalidValue("recording.lowSpaceGb", result.LowSpaceGb.ToString(CultureInfo.InvariantCulture), defaults.LowSpaceGb.ToString(CultureInfo.InvariantCulture));
            result = result with { LowSpaceGb = defaults.LowSpaceGb };
        }

        if (result.DefaultSourceIds.Count > 32 || result.DefaultSourceIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 512))
        {
            LogInvalidValue("recording.defaultSourceIds", "list", "[]");
            result = result with { DefaultSourceIds = [] };
        }

        if (result.Storage.Validate() is not null)
        {
            LogInvalidValue("recording.storage", result.Storage.Codec ?? "null", StorageSettings.Flac);
            result = result with { Storage = new StorageSettings() };
        }
        else if (!result.Storage.IsLossy && result.Storage.BitrateKbps is not null)
        {
            result = result with { Storage = result.Storage with { BitrateKbps = null } };
        }

        return result;
    }

    private static void Validate(AppSettings settings)
    {
        if (!ThemePreference.IsValid(settings.Theme))
        {
            throw new ArgumentException(
                $"Theme '{settings.Theme}' is not one of {string.Join(", ", ThemePreference.All)}.", nameof(settings));
        }

        if (!ListDensity.IsValid(settings.ListDensity))
        {
            throw new ArgumentException(
                $"List density '{settings.ListDensity}' is not one of {string.Join(", ", ListDensity.All)}.", nameof(settings));
        }

        if (settings.LibraryPath is not null && !Path.IsPathFullyQualified(settings.LibraryPath))
        {
            throw new ArgumentException("The library path must be a full path such as D:\\Memento Library.", nameof(settings));
        }

        if (settings.Recording is null)
        {
            throw new ArgumentException("Recording settings are missing.", nameof(settings));
        }

        if (settings.Recording.Validate() is { } problem)
        {
            throw new ArgumentException(problem, nameof(settings));
        }

        if (settings.Transcription is null || settings.Speakers is null || settings.History is null)
        {
            throw new ArgumentException("Transcription, speaker or history settings are missing.", nameof(settings));
        }

        if ((settings.Transcription.Validate() ?? settings.Speakers.Validate() ?? settings.History.Validate()) is { } m2Problem)
        {
            throw new ArgumentException(m2Problem, nameof(settings));
        }
    }

    private async Task WriteAtomicAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SettingsJsonContext.Default.AppSettings, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        await AtomicReplace.ReplaceAsync(temporaryPath, _filePath, cancellationToken);
    }

    private string SetAsideUnreadableFile()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = $"{_filePath}.unreadable-{stamp}";
        File.Move(_filePath, target, overwrite: true);
        return target;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Settings file {Path} could not be read and was kept as {SetAsidePath}; defaults are in use")]
    private partial void LogUnreadable(string path, string setAsidePath, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Settings field {Field} had unsupported value {Value}; using {Fallback}")]
    private partial void LogInvalidValue(string field, string value, string fallback);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Settings field {Field} was saved before 2.0, when it did nothing; it starts off (schema 1 to 2)")]
    private partial void LogNotCarriedOver(string field);
}
