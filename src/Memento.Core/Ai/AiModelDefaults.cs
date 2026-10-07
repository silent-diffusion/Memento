namespace Memento.Core.Ai;

/// <summary>
/// The cloud models used when Settings has no override, and the ones Settings offers (the field also accepts any other
/// model id the provider serves). Anthropic: Claude Opus 5.5, the most capable generally available model and the
/// claude-api skill's default; Claude Fable 5.1 is offered as Anthropic's most capable model (higher price, always
/// thinks, needs 30-day data retention). OpenAI: the model M4a chose from OpenAI's model documentation.
/// </summary>
public static class AiModelDefaults
{
    public const string AnthropicModel = "claude-opus-5-5";
    public const string AnthropicMostCapableModel = "claude-fable-5-1";
    public const string OpenAiModel = "gpt-6-astra";

    public static IReadOnlyList<string> AnthropicModels { get; } = [AnthropicModel, AnthropicMostCapableModel];

    public static IReadOnlyList<string> OpenAiModels { get; } = [OpenAiModel];

    /// <summary>Longest model id accepted in Settings.</summary>
    public const int MaxModelIdLength = 100;

    /// <summary>A plausible model id: letters, digits and <c>. _ : / -</c>, starting with a letter or digit.</summary>
    public static bool IsValidModelId(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxModelIdLength
        && char.IsAsciiLetterOrDigit(id[0])
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '/' or '-');
}
