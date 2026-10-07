using System.Text.RegularExpressions;

namespace Memento.AI.Http;

/// <summary>
/// Removes API keys from any text that might be logged or shown: the exact key, and anything shaped like an
/// Anthropic or OpenAI key (some providers echo a masked key in their 401 message).
/// </summary>
internal static partial class SecretRedactor
{
    public const string Mask = "[key removed]";

    public static string? Redact(string? text, string? key = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (!string.IsNullOrEmpty(key))
        {
            text = text.Replace(key, Mask, StringComparison.Ordinal);
        }

        return KeyShape().Replace(text, Mask);
    }

    [GeneratedRegex(@"\b(sk|sess)-[A-Za-z0-9_\-\*\.]{6,}", RegexOptions.CultureInvariant)]
    private static partial Regex KeyShape();
}
