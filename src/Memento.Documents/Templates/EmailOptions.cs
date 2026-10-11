namespace Memento.Documents.Templates;

/// <summary>
/// 2.0: the Follow-up email module's own settings: the tone of its fixed sentences (greeting, opening, closing) and the
/// name it is signed with. The facts in it (recap, decisions, action items) come from the transcript whatever the tone.
/// </summary>
public sealed record EmailOptions
{
    public const string Neutral = "neutral";
    public const string Warm = "warm";
    public const string Brief = "brief";

    /// <summary>The longest sign-off name.</summary>
    public const int MaxSignOffLength = 80;

    public static IReadOnlyList<string> Tones { get; } = [Neutral, Warm, Brief];

    public static EmailOptions Default { get; } = new();

    /// <summary>One of <see cref="Tones"/>.</summary>
    public string Tone { get; init; } = Neutral;

    /// <summary>The name under the closing; <c>null</c> signs with the closing alone.</summary>
    public string? SignOff { get; init; }

    /// <summary>The first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate()
    {
        if (!Tones.Contains(Tone, StringComparer.Ordinal))
        {
            return $"The email tone '{Tone}' is not one of neutral, warm or brief.";
        }

        if (SignOff is { } name && (name.Length > MaxSignOffLength || name.Any(char.IsControl)))
        {
            return $"The sign-off name must be one line of at most {MaxSignOffLength} characters.";
        }

        return null;
    }
}
