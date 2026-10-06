using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Settings;

/// <summary>
/// Stores <see cref="AppSettings"/> as JSON. Writes go to <c>settings.json.tmp</c>, are flushed to disk,
/// then moved over the real file, so a crash mid-write never leaves a half-written settings file.
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

        return normalized;
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

        File.Move(temporaryPath, _filePath, overwrite: true);
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
}
