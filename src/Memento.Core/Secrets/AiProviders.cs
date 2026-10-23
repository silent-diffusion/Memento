namespace Memento.Core.Secrets;

/// <summary>
/// The AI providers a key can be saved for (Settings › AI and privacy › Providers): the two cloud services and, from
/// 2.5.0, "Your own server", whose key is optional (a bearer token for a server that asks for one).
/// </summary>
public static class AiProviders
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";

    /// <summary>"Your own server": an OpenAI-compatible server the user runs (2.5.0).</summary>
    public const string Server = "server";

    public static IReadOnlyList<string> All { get; } = [Anthropic, OpenAi, Server];

    public static bool IsValid(string? provider) => provider is not null && All.Contains(provider, StringComparer.Ordinal);

    /// <summary>The name the interface uses: "Claude", "ChatGPT", "Your own server".</summary>
    public static string DisplayName(string provider) => provider switch
    {
        Anthropic => "Claude",
        Server => "Your own server",
        _ => "ChatGPT",
    };
}
