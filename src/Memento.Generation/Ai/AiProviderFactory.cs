using Memento.AI;
using Memento.AI.Anthropic;
using Memento.AI.Http;
using Memento.AI.Local;
using Memento.AI.OpenAI;
using Memento.Core.Ai;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging;

namespace Memento.Generation.Ai;

/// <summary>
/// The production providers: Claude and ChatGPT over the shared HTTP client (created on the first cloud generation; the
/// local model never touches it), the local model through Memento.Worker. A cloud provider sends exactly the model chosen
/// in Settings, with that model's limits and only the settings it takes (<see cref="AiModelDefaults.Describe"/>); nothing
/// stands in for it.
/// </summary>
public sealed class AiProviderFactory(Lazy<AiHttpClient> http, ISecretReader secrets, WorkerClient workers, ILoggerFactory loggers, TimeProvider? time = null) : IAiProviderFactory
{
    public IAiProvider CreateCloud(string id, string model) => id switch
    {
        ProviderIds.Anthropic => new AnthropicProvider(http.Value.Client, secrets, AnthropicOptionsFor(model), loggers.CreateLogger<AnthropicProvider>(), time),
        ProviderIds.OpenAi => new OpenAiProvider(http.Value.Client, secrets, OpenAiOptionsFor(model), loggers.CreateLogger<OpenAiProvider>(), time),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Not a cloud provider."),
    };

    public static AnthropicOptions AnthropicOptionsFor(string model) =>
        AnthropicOptions.For(AiModelDefaults.Describe(ProviderIds.Anthropic, model), TestEndpoints.Loopback(TestEndpoints.AnthropicVariable));

    public static OpenAiOptions OpenAiOptionsFor(string model) =>
        OpenAiOptions.For(AiModelDefaults.Describe(ProviderIds.OpenAi, model), TestEndpoints.Loopback(TestEndpoints.OpenAiVariable));

    public IAiProvider CreateLocal(LocalModelEntry model, string modelPath, LocalAiOptions options, Func<long?> freeVram) =>
        new LocalAiProvider(model, modelPath, new WorkerLocalLlmJobClient(workers), EstimatingTokenCounter.Generic, freeVram, options);
}
