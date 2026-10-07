namespace Memento.AI.Tests.Local;

/// <summary>Real-model tests run one at a time: one model in memory, one GPU stage at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalLlmHardwareGroup
{
    public const string Name = "Local LLM hardware";
}
