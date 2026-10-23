using Memento.AI.Local;
using Memento.Core.Secrets;

namespace Memento.Generation.Ai;

/// <summary>The providers a template or Settings can name.</summary>
public static class ProviderIds
{
    public const string Anthropic = AiProviders.Anthropic;
    public const string OpenAi = AiProviders.OpenAi;

    /// <summary>"Your own server" (2.5.0): an OpenAI-compatible server the user runs.</summary>
    public const string Server = AiProviders.Server;
    public const string Local = LocalAiProvider.ProviderId;

    /// <summary>In the order <c>providers.list</c> lists them; "Your own server" (2.5.0) last, after the local model.</summary>
    public static IReadOnlyList<string> All { get; } = [Anthropic, OpenAi, Local, Server];

    public static bool IsValid(string? id) => id is not null && All.Contains(id, StringComparer.Ordinal);

    /// <summary>Claude or ChatGPT: another company's service with a model from Memento's list. Not whether text leaves the PC (see <see cref="ProviderStatus.LeavesPc"/>).</summary>
    public static bool IsCloud(string id) => id is Anthropic or OpenAi;

    public static string DisplayName(string id) => id switch
    {
        Local => LocalAiProvider.ProviderName,
        _ => AiProviders.DisplayName(id),
    };

    public static string Vendor(string id) => id switch
    {
        Anthropic => "Anthropic",
        OpenAi => "OpenAI",
        Server => "Your own server",
        _ => "This PC",
    };

    /// <summary>The bridge's <c>kind</c>: <c>cloud</c>, <c>server</c> or <c>local</c>.</summary>
    public static string Kind(string id) => id switch
    {
        Local => "local",
        Server => "server",
        _ => "cloud",
    };
}
