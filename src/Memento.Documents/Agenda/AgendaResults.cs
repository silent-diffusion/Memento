using System.Globalization;
using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda;

/// <summary>Builds the final <see cref="AgendaParseResult"/>: the item limit, the no-items error, the warnings in order.</summary>
internal static class AgendaResults
{
    public static AgendaParseResult Create(
        AgendaSourceKind kind,
        AgendaParseOptions options,
        StructuredAgenda structured,
        IEnumerable<AgendaParseWarning>? formatWarnings = null,
        string? ocrEngine = null)
    {
        var items = structured.Items;
        var warnings = new List<AgendaParseWarning>();
        warnings.AddRange(formatWarnings ?? []);
        warnings.AddRange(structured.Warnings);

        if (items.Count == 0)
        {
            throw AgendaErrors.NoItems(options);
        }

        if (items.Count > options.MaxItems)
        {
            var rest = items.Skip(options.MaxItems).ToList();
            warnings.Add(new AgendaParseWarning(
                AgendaWarningCodes.TooManyItems,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"An agenda can hold {options.MaxItems} items, so the first {options.MaxItems} were kept and {rest.Count} more were left out. Remove items you do not need, then add the rest here."),
                string.Join('\n', rest.Select(i => i.Text)),
                rest[0].Location));
            items = items.Take(options.MaxItems).ToList();
        }

        var sourceName = options.SourceKind == AgendaSourceKind.PastedText || string.IsNullOrWhiteSpace(options.FileName)
            ? null
            : Path.GetFileName(options.FileName.Trim());
        return new AgendaParseResult(items, kind, sourceName, structured.Title, warnings, ocrEngine);
    }

    public static string FileExtension(AgendaParseOptions options) =>
        string.IsNullOrWhiteSpace(options.FileName) ? string.Empty : Path.GetExtension(options.FileName.Trim()).ToLowerInvariant();

    public static bool HasExtension(string fileName, params string[] extensions)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        return extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasContentType(string? contentType, params string[] types)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var bare = contentType.Split(';')[0].Trim();
        return types.Any(t => string.Equals(t, bare, StringComparison.OrdinalIgnoreCase));
    }
}
