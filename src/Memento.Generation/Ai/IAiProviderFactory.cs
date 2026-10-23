using Memento.AI;
using Memento.AI.Local;
using Memento.AI.Server;

namespace Memento.Generation.Ai;

/// <summary>Constructs providers when a generation runs (never earlier), so a refused generation constructs none.</summary>
public interface IAiProviderFactory
{
    /// <param name="id"><c>anthropic</c> or <c>openai</c>.</param>
    IAiProvider CreateCloud(string id, string model);

    /// <summary>2.5.0: "Your own server" at the address, model and context from Settings.</summary>
    IAiProvider CreateServer(ServerOptions options);

    IAiProvider CreateLocal(LocalModelEntry model, string modelPath, LocalAiOptions options, Func<long?> freeVram);
}
