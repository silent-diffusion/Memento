using Memento.AI.Anthropic;
using Memento.AI.Http;
using Memento.AI.OpenAI;
using Memento.Core.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Memento.AI;

/// <summary>Registers the cloud providers. The bridge wiring and the local provider's worker client are added by the app.</summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="AnthropicProvider"/> and <see cref="OpenAiProvider"/> (each also as an <see cref="IAiProvider"/>),
    /// the shared <see cref="AiHttpClient"/> and an <see cref="ISecretReader"/> over the registered <see cref="ISecretStore"/>.
    /// </summary>
    public static IServiceCollection AddMementoCloudAi(this IServiceCollection services, AnthropicOptions? anthropic = null, OpenAiOptions? openAi = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<AiHttpClient>();
        services.TryAddSingleton<ISecretReader>(sp => new SecretStoreReader(sp.GetRequiredService<ISecretStore>()));
        services.TryAddSingleton(anthropic ?? new AnthropicOptions());
        services.TryAddSingleton(openAi ?? new OpenAiOptions());
        services.TryAddSingleton(sp => new AnthropicProvider(
            sp.GetRequiredService<AiHttpClient>().Client,
            sp.GetRequiredService<ISecretReader>(),
            sp.GetRequiredService<AnthropicOptions>(),
            sp.GetRequiredService<ILogger<AnthropicProvider>>(),
            sp.GetService<TimeProvider>()));
        services.TryAddSingleton(sp => new OpenAiProvider(
            sp.GetRequiredService<AiHttpClient>().Client,
            sp.GetRequiredService<ISecretReader>(),
            sp.GetRequiredService<OpenAiOptions>(),
            sp.GetRequiredService<ILogger<OpenAiProvider>>(),
            sp.GetService<TimeProvider>()));
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<AnthropicProvider>());
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<OpenAiProvider>());
        return services;
    }
}
