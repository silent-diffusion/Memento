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
        foreach (var entry in document.Models)
        {
            Check(entry, seen);
        }

        return new ModelCatalog(document.Models);
    }

    private static void Check(ModelCatalogEntry entry, HashSet<string> seen)
    {
        string? problem = null;
        if (!IdPattern().IsMatch(entry.Id ?? string.Empty))
        {
            problem = "its id must be lower-case letters, digits and dashes";
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
        else if (string.IsNullOrWhiteSpace(entry.FileName) || entry.FileName.IndexOfAny(['/', '\\', ':']) >= 0 || entry.FileName.Trim('.').Length == 0)
        {
            problem = "fileName must be a plain file name";
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

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
