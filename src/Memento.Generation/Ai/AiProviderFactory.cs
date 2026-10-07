using Memento.AI;
using Memento.AI.Anthropic;
using Memento.AI.Http;
using Memento.AI.Local;
using Memento.AI.OpenAI;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging;

namespace Memento.Generation.Ai;

/// <summary>
/// The production providers: Claude and ChatGPT over the shared HTTP client (created on the first cloud generation; the
/// local model never touches it), the local model through Memento.Worker.
/// </summary>
public sealed class AiProviderFactory(Lazy<AiHttpClient> http, ISecretReader secrets, WorkerClient workers, ILoggerFactory loggers, TimeProvider? time = null) : IAiProviderFactory
{
    public IAiProvider CreateCloud(string id, string model) => id switch
    {
        ProviderIds.Anthropic => new AnthropicProvider(http.Value.Client, secrets, new AnthropicOptions { Model = model, BaseUrl = TestEndpoints.Loopback(TestEndpoints.AnthropicVariable) ?? AnthropicOptions.DefaultBaseUrl }, loggers.CreateLogger<AnthropicProvider>(), time),
        ProviderIds.OpenAi => new OpenAiProvider(http.Value.Client, secrets, new OpenAiOptions { Model = model, BaseUrl = TestEndpoints.Loopback(TestEndpoints.OpenAiVariable) ?? OpenAiOptions.DefaultBaseUrl }, loggers.CreateLogger<OpenAiProvider>(), time),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Not a cloud provider."),
    };

    public IAiProvider CreateLocal(LocalModelEntry model, string modelPath, LocalAiOptions options, Func<long?> freeVram) =>
        new LocalAiProvider(model, modelPath, new WorkerLocalLlmJobClient(workers), EstimatingTokenCounter.Generic, freeVram, options);
}
