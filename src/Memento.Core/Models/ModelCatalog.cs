using System.Text.Json;
using System.Text.RegularExpressions;

namespace Memento.Core.Models;

/// <summary>
/// The engines and models the model manager can install (ARCHITECTURE.md §1, "Engine philosophy"). The built-in
/// catalog is the embedded <c>catalog.json</c>; tests parse their own.
/// </summary>
public sealed partial class ModelCatalog
{
    private const string ResourceName = "Memento.Core.Models.catalog.json";
    private static readonly Lazy<ModelCatalog> BuiltIn = new(LoadBuiltIn);

    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    // Files the model manager keeps beside a model; a catalog file name must not collide with one of them.
    private static readonly string[] ManagerSuffixes = [".part", ".tmp", ModelVerifiedStamp.Suffix];

    private readonly Dictionary<string, ModelCatalogEntry> _byId;

    private ModelCatalog(IReadOnlyList<ModelCatalogEntry> entries)
    {
        Entries = entries;
        _byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    /// <summary>The catalog that ships with this build.</summary>
    public static ModelCatalog Default => BuiltIn.Value;

    /// <summary>Every model, in catalog order.</summary>
    public IReadOnlyList<ModelCatalogEntry> Entries { get; }

    public ModelCatalogEntry? Find(string? id) => id is not null && _byId.TryGetValue(id, out var entry) ? entry : null;

    public IEnumerable<ModelCatalogEntry> OfKind(string kind) => Entries.Where(e => e.Kind == kind);

    /// <summary>
    /// The same catalog downloading from a mirror on this PC (<c>&lt;mirror&gt;/&lt;engine&gt;/&lt;file&gt;</c>), for the
    /// hidden <c>--model-mirror</c> test switch. Sizes and SHA-256 stay as published, so a mirror cannot change what
    /// gets installed. Only loopback addresses are accepted.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="mirror"/> is not an http(s) address on this PC.</exception>
    public ModelCatalog WithMirror(Uri mirror)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        if (!mirror.IsAbsoluteUri || mirror.Scheme is not ("http" or "https") || !mirror.IsLoopback)
        {
            throw new ArgumentException($"A model mirror must be an http address on this PC (localhost or 127.0.0.1), not '{mirror}'.", nameof(mirror));
        }

        var root = mirror.AbsoluteUri.TrimEnd('/');
        return new ModelCatalog(Entries.Select(e => e with { Url = $"{root}/{Uri.EscapeDataString(e.Engine)}/{Uri.EscapeDataString(e.FileName)}" }).ToList());
    }

    /// <summary>Parses and checks a catalog.</summary>
    /// <exception cref="InvalidDataException">The JSON is not a valid catalog; the message names the problem.</exception>
    public static ModelCatalog Parse(string json)
    {
        ModelCatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, ModelJsonContext.Default.ModelCatalogDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The model catalog is not valid JSON: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new InvalidDataException("The model catalog is empty.");
        }

        if (document.SchemaVersion != ModelCatalogDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"The model catalog has schema {document.SchemaVersion}; this build reads schema {ModelCatalogDocument.CurrentSchemaVersion}.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Windows file names are case-insensitive: two entries must never share a file.
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.Models)
        {
            Check(entry, seen);
            if (!files.Add(entry.Engine + "/" + entry.FileName))
            {
                throw new InvalidDataException($"Model catalog entry '{entry.Id}' is not valid: another entry of engine '{entry.Engine}' already uses the file name '{entry.FileName}'.");
            }
        }

        return new ModelCatalog(document.Models);
    }

    private static bool IsPlainFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 200
            || name.Any(c => c < 0x20 || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            || name.Trim('.').Length == 0
            || name.EndsWith('.')
            || name.EndsWith(' ')
            || name.StartsWith(' '))
        {
            return false;
        }

        // "nul", "nul.bin" and "COM1.onnx" all open a device on Windows.
        var stem = name.Split('.')[0].TrimEnd(' ');
        if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ManagerSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase))
            && !name.Contains(".corrupt-", StringComparison.OrdinalIgnoreCase);
    }

    private static void Check(ModelCatalogEntry entry, HashSet<string> seen)
    {
        string? problem = null;
        if (!IdPattern().IsMatch(entry.Id ?? string.Empty))
        {
            problem = "its id must be lower-case letters, digits, dashes and dots";
        }
        else if (!seen.Add(entry.Id!))
        {
            problem = "the id is listed twice";
        }
        else if (!ModelKinds.All.Contains(entry.Kind, StringComparer.Ordinal))
        {
            problem = $"kind '{entry.Kind}' is not one of {string.Join(", ", ModelKinds.All)}";
        }
        else if (!Sha256Pattern().IsMatch(entry.Sha256 ?? string.Empty))
        {
            problem = "sha256 must be 64 lower-case hex characters";
        }
        else if (entry.SizeBytes <= 0)
        {
            problem = "sizeBytes must be positive";
        }
        else if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
        {
            problem = "url must be an https address";
        }
        else if (!ModelDownloadHosts.IsAllowedCatalogUrl(uri))
        {
            problem = $"url must be on {string.Join(" or ", ModelDownloadHosts.CatalogHosts)}, not {uri.Host}";
        }
        else if (!IsPlainFileName(entry.FileName))
        {
            problem = "fileName must be a plain Windows file name (no folders, device names such as NUL, trailing dots or spaces, or the names Memento keeps beside a model)";
        }
        else if (string.IsNullOrWhiteSpace(entry.Engine) || !IdPattern().IsMatch(entry.Engine))
        {
            problem = "engine must be a folder-safe name";
        }
        else if (entry.RunsOn is not ("gpu" or "cpu" or "either"))
        {
            problem = "runsOn must be gpu, cpu or either";
        }
        else if (entry.Kind == ModelKinds.Speakers && entry.Role is not (ModelRoles.Segmentation or ModelRoles.Embedding))
        {
            problem = "a speaker model needs role segmentation or embedding";
        }

        if (problem is not null)
        {
            throw new InvalidDataException($"Model catalog entry '{entry.Id}' is not valid: {problem}.");
        }
    }

    private static ModelCatalog LoadBuiltIn()
    {
        using var stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"The built-in model catalog ({ResourceName}) is missing from this build.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    [GeneratedRegex("^[a-z0-9]+([.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
