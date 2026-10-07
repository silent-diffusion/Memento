using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace Memento.Documents.Model.Storage;

/// <summary>
/// A folder of <c>&lt;id&gt;.json</c> files layered over a fixed set of built-in items. A file whose id is a built-in id
/// is the user's customised copy of it; resetting deletes that file. Writes are atomic and serialised.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The write lock never allocates a wait handle; the stores live as long as the app.")]
internal sealed partial class JsonItemStore<T>
    where T : class
{
    private readonly string _folder;
    private readonly string _noun;
    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly List<T> _builtIns;
    private readonly StoreItemAccessors<T> _access;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonItemStore(string folder, string noun, JsonTypeInfo<T> typeInfo, IReadOnlyList<T> builtIns, StoreItemAccessors<T> access, ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        _folder = folder;
        _noun = noun;
        _typeInfo = typeInfo;
        _builtIns = builtIns.Select(b => access.Mark(b, true, false)).ToList();
        _access = access;
        _logger = logger;
    }

    public bool IsBuiltIn(string id) => _builtIns.Any(b => string.Equals(_access.Id(b), id, StringComparison.Ordinal));

    /// <summary>Built-ins first in their fixed order (customised copies in their place), then the user's items by name.</summary>
    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken)
    {
        var stored = new Dictionary<string, T>(StringComparer.Ordinal);
        if (Directory.Exists(_folder))
        {
            foreach (var path in Directory.EnumerateFiles(_folder, "*.json").Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = Path.GetFileNameWithoutExtension(path);
                if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) || !StoreIds.IsValid(id))
                {
                    continue;
                }

                try
                {
                    stored[id] = await ReadAsync(path, id, cancellationToken);
                }
                catch (DocumentStoreException ex)
                {
                    if (_logger is not null)
                    {
                        LogSkipped(_logger, ex, _noun, Path.GetFileName(path));
                    }
                }
            }
        }

        var result = new List<T>(_builtIns.Count + stored.Count);
        foreach (var builtIn in _builtIns)
        {
            var id = _access.Id(builtIn);
            result.Add(stored.Remove(id, out var custom) ? custom : builtIn);
        }

        result.AddRange(stored.Values
            .OrderBy(_access.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(_access.Id, StringComparer.Ordinal));
        return result;
    }

    /// <summary>The item, its customised copy when it is a built-in that was changed, or <c>null</c>.</summary>
    /// <exception cref="DocumentStoreException">The file exists but cannot be read.</exception>
    public async Task<T?> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (!StoreIds.IsValid(id))
        {
            return null;
        }

        var path = PathOf(id);
        if (File.Exists(path))
        {
            return await ReadAsync(path, id, cancellationToken);
        }

        return _builtIns.FirstOrDefault(b => string.Equals(_access.Id(b), id, StringComparison.Ordinal));
    }

    public async Task<T> SaveAsync(T item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var id = _access.Id(item);
        if (!StoreIds.IsValid(id))
        {
            throw new DocumentStoreException(
                StoreErrorCodes.InvalidId,
                $"The {_noun} id \"{id}\" can only use lower-case letters, digits and hyphens (up to {StoreIds.MaxLength.ToString(CultureInfo.InvariantCulture)}). Nothing was saved.");
        }

        var builtIn = IsBuiltIn(id);
        var marked = _access.Mark(item, builtIn, builtIn);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(marked, _typeInfo);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await AtomicFile.WriteAllBytesAsync(PathOf(id), bytes, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        return marked;
    }

    /// <summary>Copies an item under a new unique id; the copy is never built in.</summary>
    public async Task<T> DuplicateAsync(string id, string? newName, CancellationToken cancellationToken)
    {
        var source = await GetAsync(id, cancellationToken) ?? throw NotFound(id);
        var name = string.IsNullOrWhiteSpace(newName) ? $"{_access.Name(source)} (copy)" : newName.Trim();
        var existing = (await ListAsync(cancellationToken)).Select(_access.Id).ToHashSet(StringComparer.Ordinal);
        var newId = StoreIds.Unique(StoreIds.Slug(name), candidate => existing.Contains(candidate) || File.Exists(PathOf(candidate)));
        var copy = _access.Mark(_access.WithIdAndName(source, newId, name), false, false);
        return await SaveAsync(copy, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        if (IsBuiltIn(id))
        {
            throw new DocumentStoreException(
                StoreErrorCodes.BuiltInCannotBeDeleted,
                $"The built-in {_noun} \"{id}\" cannot be deleted. It was left as it is; use Reset to undo changes to it, or duplicate it.");
        }

        var path = PathOf(id);
        if (!StoreIds.IsValid(id) || !File.Exists(path))
        {
            throw NotFound(id);
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            File.Delete(path);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Removes the customised copy of a built-in item and returns the built-in.</summary>
    public async Task<T> ResetBuiltInAsync(string id, CancellationToken cancellationToken)
    {
        var builtIn = _builtIns.FirstOrDefault(b => string.Equals(_access.Id(b), id, StringComparison.Ordinal))
            ?? throw new DocumentStoreException(
                StoreErrorCodes.NotBuiltIn,
                $"\"{id}\" is not a built-in {_noun}, so there is nothing to reset it to. Nothing was changed.");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var path = PathOf(id);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _writeLock.Release();
        }

        return builtIn;
    }

    private string PathOf(string id) => Path.Combine(_folder, id + ".json");

    private async Task<T> ReadAsync(string path, string id, CancellationToken cancellationToken)
    {
        T? item;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
            item = await JsonSerializer.DeserializeAsync(stream, _typeInfo, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new DocumentStoreException(
                StoreErrorCodes.Unreadable,
                $"The {_noun} file {Path.GetFileName(path)} is not readable ({ex.Message}). It was left in place; fix or remove it, or reset the {_noun}.",
                ex);
        }

        if (item is null)
        {
            throw new DocumentStoreException(StoreErrorCodes.Unreadable, $"The {_noun} file {Path.GetFileName(path)} is empty. It was left in place.");
        }

        if (_access.SchemaVersion(item) > _access.CurrentSchemaVersion)
        {
            throw new DocumentStoreException(
                StoreErrorCodes.Unreadable,
                $"The {_noun} file {Path.GetFileName(path)} was written by a newer version of Memento. It was left unchanged; update Memento to use it.");
        }

        if (!string.Equals(_access.Id(item), id, StringComparison.Ordinal))
        {
            item = _access.WithIdAndName(item, id, _access.Name(item));
        }

        var builtIn = IsBuiltIn(id);
        return _access.Mark(item, builtIn, builtIn);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped the unreadable {Noun} file {File}")]
    private static partial void LogSkipped(ILogger logger, Exception exception, string noun, string file);

    private DocumentStoreException NotFound(string id) =>
        new(StoreErrorCodes.NotFound, $"There is no {_noun} with the id \"{id}\". Nothing was changed.");
}
