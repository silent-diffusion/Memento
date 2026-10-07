using Memento.AI;
using Memento.AI.Local;

namespace Memento.Generation.Ai;

/// <summary>Constructs providers when a generation runs (never earlier), so a refused generation constructs none.</summary>
public interface IAiProviderFactory
{
    /// <param name="id"><c>anthropic</c> or <c>openai</c>.</param>
    IAiProvider CreateCloud(string id, string model);

    IAiProvider CreateLocal(LocalModelEntry model, string modelPath, LocalAiOptions options, Func<long?> freeVram);
}
