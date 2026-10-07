namespace Memento.Documents.Model;

/// <summary>The factor each <see cref="TextSize"/> applies to the style's base size, in every output.</summary>
public static class TextSizeExtensions
{
    public const double SmallerFactor = 0.875;
    public const double NormalFactor = 1.0;
    public const double LargerFactor = 1.15;

    public static double Factor(this TextSize size) => size switch
    {
        TextSize.Smaller => SmallerFactor,
        TextSize.Larger => LargerFactor,
        _ => NormalFactor,
    };

    /// <summary>The lower-case name used in markup and JSON: <c>smaller</c>, <c>normal</c>, <c>larger</c>.</summary>
    public static string Name(this TextSize size) => size switch
    {
        TextSize.Smaller => "smaller",
        TextSize.Larger => "larger",
        _ => "normal",
    };

    /// <summary>Parses a markup or JSON name; anything unknown is <see cref="TextSize.Normal"/>.</summary>
    public static TextSize ParseName(string? name) => name switch
    {
        "smaller" => TextSize.Smaller,
        "larger" => TextSize.Larger,
        _ => TextSize.Normal,
    };
}
