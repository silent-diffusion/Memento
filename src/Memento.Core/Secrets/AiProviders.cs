namespace Memento.Core.Secrets;

/// <summary>The external AI providers a key can be saved for (Settings › AI and privacy › Providers).</summary>
public static class AiProviders
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";

    public static IReadOnlyList<string> All { get; } = [Anthropic, OpenAi];

    public static bool IsValid(string? provider) => provider is not null && All.Contains(provider, StringComparer.Ordinal);

    /// <summary>The name the interface uses: "Claude", "ChatGPT".</summary>
    public static string DisplayName(string provider) => provider == Anthropic ? "Claude" : "ChatGPT";
}
