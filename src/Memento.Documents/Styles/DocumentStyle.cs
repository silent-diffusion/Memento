using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Styling;

/// <summary>
/// A document style: exactly the Style editor's settings (DESIGN.md §13, schema v1). A style changes how a document looks,
/// never what it says. Word and PDF follow it; Markdown ignores it. <see cref="StyleMetrics"/> turns it into sizes and colours.
/// </summary>
public sealed record DocumentStyle
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    // Type
    public Typeface HeadingTypeface { get; init; } = Typeface.Sans;

    public Typeface BodyTypeface { get; init; } = Typeface.Sans;

    public BaseSize BaseSize { get; init; } = BaseSize.Normal;

    public HeadingCase HeadingCase { get; init; } = HeadingCase.Normal;

    public bool NumberedHeadings { get; init; }

    // Colour (body text is fixed Ink)
    public HeadingColor HeadingColor { get; init; } = HeadingColor.Ink;

    public bool TableHeaderFill { get; init; }

    // Structure
    public bool RuleUnderTitle { get; init; }

    public bool LinesBetweenSections { get; init; }

    public Spacing Spacing { get; init; } = Spacing.Normal;

    // Page
    public PaperSize Paper { get; init; } = PaperSize.Letter;

    public bool PageNumbers { get; init; } = true;

    /// <summary>The recording title and date on every page.</summary>
    public bool RunningHeader { get; init; }

    /// <summary>Set by the store: one of the presets that ship with Memento.</summary>
    public bool BuiltIn { get; init; }

    /// <summary>Set by the store: a built-in preset the user has changed (Reset is available). Not written to disk.</summary>
    [JsonIgnore]
    public bool Customized { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
