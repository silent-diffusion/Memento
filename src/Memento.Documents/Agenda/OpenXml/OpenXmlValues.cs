using System.Globalization;
using DocumentFormat.OpenXml;

namespace Memento.Documents.Agenda.OpenXml;

/// <summary>
/// Reads typed Open XML attribute values without throwing: the SDK's <c>.Value</c> getters throw
/// <see cref="FormatException"/> or <see cref="OverflowException"/> on text such as <c>w:gridSpan="x"</c> or
/// <c>r="4294967296"</c>. A value that does not parse is treated as absent, as Word and Excel do.
/// </summary>
internal static class OpenXmlValues
{
    public static int? Int(Int32Value? value) =>
        value?.InnerText is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    public static uint? UInt(UInt32Value? value) =>
        value?.InnerText is { } text && uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    public static T? Enum<T>(EnumValue<T>? value)
        where T : struct, IEnumValue, IEnumValueFactory<T>
    {
        if (value?.InnerText is null)
        {
            return null;
        }

        try
        {
            return value.Value;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
