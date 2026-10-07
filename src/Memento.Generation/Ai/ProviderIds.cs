using Memento.AI.Local;
using Memento.Core.Secrets;

namespace Memento.Generation.Ai;

/// <summary>The providers a template or Settings can name.</summary>
public static class ProviderIds
{
    public const string Anthropic = AiProviders.Anthropic;
    public const string OpenAi = AiProviders.OpenAi;
    public const string Local = LocalAiProvider.ProviderId;

    public static IReadOnlyList<string> All { get; } = [Anthropic, OpenAi, Local];

    public static bool IsValid(string? id) => id is not null && All.Contains(id, StringComparer.Ordinal);

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
        _ => "This PC",
    };
}
