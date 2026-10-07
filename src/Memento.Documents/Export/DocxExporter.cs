using Memento.Documents.Export.Docx;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;
using Memento.Documents.Styling;

namespace Memento.Documents.Export;

/// <summary>
/// Word export via Open XML (ARCHITECTURE.md §1): styles mapped from the <see cref="DocumentStyle"/> (fonts, sizes, heading
/// colour, case and numbering, rules), side-by-side rows as a borderless table, lists, tables with header fill, chips as
/// shaded runs, quotes, timelines, the Full transcript as a compact table, timestamps as footnotes, the running header and
/// page numbers in the section, and the paper size.
/// </summary>
public sealed class DocxExporter
{
    private readonly ModuleCatalog _catalog;

    public DocxExporter(ModuleCatalog? catalog = null)
    {
        _catalog = catalog ?? ModuleCatalog.Default;
    }

    public byte[] Export(Document document, DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(style);
        return new DocxBuilder(document, style, _catalog).Build();
    }
}
