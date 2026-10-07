using System.Globalization;

namespace Memento.AI;

/// <summary>
/// A tokenizer-free token estimate for cloud models, calibrated to err high so a chunk budget is never exceeded:
/// words of up to seven letters count one token and longer ones one more per five letters, digits one token per
/// three, every punctuation mark, symbol and line break one, and every character of a script without spaces (CJK)
/// one; the sum is multiplied by the model's <see cref="Factor"/>. English prose comes out at about one token per
/// 3.6 characters before the factor, close to modern BPE tokenizers; the factor covers tokenizers that split more
/// finely (Claude's current tokenizer produces up to ~1.35x the tokens of older ones).
/// </summary>
public sealed class EstimatingTokenCounter : ITokenCounter
{
    /// <summary>For Claude models (current tokenizer).</summary>
    public static EstimatingTokenCounter Claude { get; } = new(1.3);

    /// <summary>For OpenAI models (o200k-style tokenizers).</summary>
    public static EstimatingTokenCounter OpenAi { get; } = new(1.1);

    /// <summary>For local models before the tokenizer is loaded.</summary>
    public static EstimatingTokenCounter Generic { get; } = new(1.25);

    public EstimatingTokenCounter(double factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 0.5);
        Factor = factor;
    }

    public double Factor { get; }

    public bool IsExact => false;

    public int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return 0;
        }

        double raw = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                var newline = false;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    newline |= text[i] == '\n';
                    i++;
                }

                raw += newline ? 1 : 0;
                continue;
            }

            if (IsUnspacedScript(c))
            {
                raw += 1;
                i++;
                continue;
            }

            if (char.IsLetter(c))
            {
                var start = i;
                var nonAscii = 0;
                while (i < text.Length && (char.IsLetter(text[i]) || (text[i] == '\'' && i + 1 < text.Length && char.IsLetter(text[i + 1]))) && !IsUnspacedScript(text[i]))
                {
                    nonAscii += text[i] > 127 ? 1 : 0;
                    i++;
                }

                var length = i - start;
                raw += 1 + (Math.Max(0, length - 7) / 5.0) + (nonAscii / 2.0);
                continue;
            }

            if (char.IsDigit(c))
            {
                var start = i;
                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }

                raw += Math.Ceiling((i - start) / 3.0);
                continue;
            }

            if (char.IsSurrogate(c))
            {
                // Emoji and other astral characters: usually two to four tokens each.
                raw += 2;
                i += char.IsHighSurrogate(c) && i + 1 < text.Length ? 2 : 1;
                continue;
            }

            raw += 1;
            i++;
        }

        return (int)Math.Ceiling(raw * Factor);
    }

    private static bool IsUnspacedScript(char c) =>
        CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.OtherLetter && c >= 0x2E80;
}
