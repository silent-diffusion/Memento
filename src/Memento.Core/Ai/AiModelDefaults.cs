using System.Globalization;
using Memento.Core.Secrets;

namespace Memento.Core.Ai;

/// <summary>
/// The cloud models Settings offers (Settings › AI and privacy, under each provider's key), the first of each list
/// being the default used when Settings has none; any other id the provider serves may be typed under "Other model…".
/// Claude (claude-api skill, model table cached 2026-10-06): Opus 5.5 is the default and the skill's default; Fable 5.1
/// is Anthropic's top tier (higher price, always thinks, needs 30-day data retention); Sonnet 5.5 and Haiku 5.5 are the
/// current Sonnet and Haiku. All four take 1M tokens and answer up to 128k; Haiku 5.5 has no server-side refusal
/// fallback, so none is asked for. ChatGPT (developers.openai.com/api/docs/models, October 2026): the three flagship
/// models, all 1,050,000 tokens with up to 128k out and <c>reasoning.effort</c> from low to max.
/// </summary>
public static class AiModelDefaults
{
    public const string AnthropicModel = "claude-opus-5-5";
    public const string AnthropicMostCapableModel = "claude-fable-5-1";
    public const string OpenAiModel = "gpt-6-astra";

    /// <summary>What Memento assumes about a Claude model it does not know: the smallest current Claude context (Claude Haiku 4.5's).</summary>
    public const int UnlistedAnthropicContextTokens = 200_000;

    /// <summary>A Claude answer limit every current and recent model accepts.</summary>
    public const int UnlistedAnthropicOutputTokens = 32_000;

    /// <summary>What Memento assumes about an OpenAI model it does not know (GPT-4o's context and answer limit).</summary>
    public const int UnlistedOpenAiContextTokens = 128_000;

    public const int UnlistedOpenAiOutputTokens = 16_384;

    /// <summary>Longest model id accepted in Settings.</summary>
    public const int MaxModelIdLength = 100;

    public static IReadOnlyList<CloudModel> AnthropicCatalog { get; } =
    [
        new(AnthropicModel, "The most capable for long transcripts. Memento's default.", 1_000_000, 128_000) { ServerSideFallback = true },
        new(AnthropicMostCapableModel, "Anthropic's top tier: the newest and most intelligent. Costs more and takes longer; the account needs 30-day data retention.", 1_000_000, 128_000) { ServerSideFallback = true },
        new("claude-sonnet-5-5", "Fast and strong, at about half the price of Opus.", 1_000_000, 128_000) { ServerSideFallback = true },
        new("claude-haiku-5-5", "The fastest and cheapest.", 1_000_000, 128_000),
    ];

    public static IReadOnlyList<CloudModel> OpenAiCatalog { get; } =
    [
        new(OpenAiModel, "OpenAI's most capable model. Memento's default.", 1_050_000, 128_000),
        new("gpt-6.1-sol", "Nearly as capable, at a lower cost.", 1_050_000, 128_000),
        new("gpt-6-luna", "OpenAI's most efficient: the fastest and cheapest.", 1_050_000, 128_000),
    ];

    public static IReadOnlyList<string> AnthropicModels { get; } = AnthropicCatalog.Select(m => m.Id).ToList();

    public static IReadOnlyList<string> OpenAiModels { get; } = OpenAiCatalog.Select(m => m.Id).ToList();

    /// <summary>A plausible model id: letters, digits and <c>. _ : / -</c>, starting with a letter or digit.</summary>
    public static bool IsValidModelId(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxModelIdLength
        && char.IsAsciiLetterOrDigit(id[0])
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '/' or '-');

    /// <summary>The models Settings lists for <paramref name="providerId"/> (<c>anthropic</c> or <c>openai</c>), the default first.</summary>
    public static IReadOnlyList<CloudModel> Catalog(string providerId) => providerId switch
    {
        AiProviders.Anthropic => AnthropicCatalog,
        AiProviders.OpenAi => OpenAiCatalog,
        _ => throw new ArgumentOutOfRangeException(nameof(providerId), providerId, "Not a cloud provider."),
    };

    public static string Default(string providerId) => Catalog(providerId)[0].Id;

    /// <summary>The model in effect: the one saved in Settings when it is a plausible id, else the default.</summary>
    public static string Effective(string providerId, string? configured) =>
        IsValidModelId(configured) ? configured! : Default(providerId);

    /// <summary>
    /// The listed model with this id, or the profile of an id typed by hand: an assumed context, an answer limit every
    /// model accepts, and no effort or fallback setting (a model Memento does not know may reject them).
    /// </summary>
    public static CloudModel Describe(string providerId, string modelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelId);
        if (Catalog(providerId).FirstOrDefault(m => m.Id == modelId) is { } listed)
        {
            return listed;
        }

        var claude = providerId == AiProviders.Anthropic;
        var context = claude ? UnlistedAnthropicContextTokens : UnlistedOpenAiContextTokens;
        var note = $"Not in Memento's list. Memento assumes it reads up to {Tokens(context)} tokens a request and sends it no effort setting.";
        return new CloudModel(modelId, note, context, claude ? UnlistedAnthropicOutputTokens : UnlistedOpenAiOutputTokens)
        {
            Listed = false,
            SendsEffort = false,
            ServerSideFallback = false,
        };
    }

    /// <summary>"1M", "1.05M", "200k", "128k".</summary>
    public static string Tokens(int tokens) => tokens >= 1_000_000
        ? (tokens / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "M"
        : (tokens / 1000).ToString(CultureInfo.InvariantCulture) + "k";
}
