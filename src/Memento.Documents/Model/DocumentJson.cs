using System.Globalization;
using System.Text.Json;
using Memento.Documents.Model.Storage;

namespace Memento.Documents.Model;

/// <summary>Reads and writes <see cref="Document"/> JSON (schema v1) and checks its structural rules.</summary>
public static class DocumentJson
{
    public static string Serialize(Document document) =>
        JsonSerializer.Serialize(document, DocumentJsonContext.Default.Document);

    public static byte[] SerializeToUtf8Bytes(Document document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, DocumentJsonContext.Default.Document);

    /// <exception cref="DocumentFormatException">Not a valid document, or written by a newer version.</exception>
    public static Document Deserialize(string json, string? sourceName = null)
    {
        Document? document;
        try
        {
            document = JsonSerializer.Deserialize(json, DocumentJsonContext.Default.Document);
        }
        catch (JsonException ex)
        {
            throw Invalid(sourceName, ex);
        }

        return Checked(document, sourceName);
    }

    /// <exception cref="DocumentFormatException">Not a valid document, or written by a newer version.</exception>
    public static async Task<Document> DeserializeAsync(Stream stream, string? sourceName, CancellationToken cancellationToken)
    {
        Document? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync(stream, DocumentJsonContext.Default.Document, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw Invalid(sourceName, ex);
        }

        return Checked(document, sourceName);
    }

    public static async Task<Document> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
        return await DeserializeAsync(stream, Path.GetFileName(path), cancellationToken);
    }

    /// <summary>Writes atomically (<c>.tmp</c> then move).</summary>
    public static Task WriteFileAsync(string path, Document document, CancellationToken cancellationToken) =>
        AtomicFile.WriteAllBytesAsync(path, SerializeToUtf8Bytes(document), cancellationToken);

    /// <summary>
    /// The structural problems of a document, as sentences: rows must hold one to three modules, module ids must be
    /// unique and non-empty. Empty when the document is sound.
    /// </summary>
    public static IReadOnlyList<string> Validate(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var r = 0; r < document.Rows.Count; r++)
        {
            var count = document.Rows[r].Modules.Count;
            if (count is < 1 or > DocumentRow.MaxModules)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"Row {r + 1} holds {count} modules; a row holds one to three."));
            }

            foreach (var module in document.Rows[r].Modules)
            {
                if (string.IsNullOrWhiteSpace(module.Id))
                {
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"A module in row {r + 1} has no id."));
                }
                else if (!ids.Add(module.Id))
                {
                    problems.Add($"The module id \"{module.Id}\" is used more than once.");
                }
            }
        }

        return problems;
    }

    private static Document Checked(Document? document, string? sourceName)
    {
        var name = sourceName ?? "The document";
        if (document is null)
        {
            throw new DocumentFormatException(DocumentFormatErrorCodes.Invalid, $"{name} is empty. Nothing was changed.");
        }

        if (document.SchemaVersion > Document.CurrentSchemaVersion)
        {
            throw new DocumentFormatException(
                DocumentFormatErrorCodes.NewerVersion,
                string.Create(CultureInfo.InvariantCulture, $"{name} was written by a newer version of Memento (schema {document.SchemaVersion}). It was left unchanged; update Memento to open it."));
        }

        var problems = Validate(document);
        if (problems.Count > 0)
        {
            throw new DocumentFormatException(DocumentFormatErrorCodes.Structure, $"{name} could not be opened: {problems[0]} Nothing was changed.");
        }

        return document with { SchemaVersion = Document.CurrentSchemaVersion };
    }

    private static DocumentFormatException Invalid(string? sourceName, JsonException ex) =>
        new(DocumentFormatErrorCodes.Invalid, $"{sourceName ?? "The document"} is not a readable Memento document ({ex.Message}). Nothing was changed.", ex);
}
