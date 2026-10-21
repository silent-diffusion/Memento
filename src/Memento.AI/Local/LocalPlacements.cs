namespace Memento.AI.Local;

/// <summary>Where a local model's weights go (<see cref="LocalMemoryPlan.Placement"/>).</summary>
public static class LocalPlacements
{
    /// <summary>Every layer on the graphics card.</summary>
    public const string Card = "card";

    /// <summary>The first blocks in RAM, the rest and the output layer on the card.</summary>
    public const string Split = "split";

    /// <summary>Mixture of experts: every layer on the card except the routed experts of some or all blocks, which stay in RAM.</summary>
    public const string Experts = "experts";

    /// <summary>In RAM on the processor (the CPU build of llama.cpp).</summary>
    public const string Ram = "ram";

    /// <summary>Does not fit.</summary>
    public const string None = "none";
}
