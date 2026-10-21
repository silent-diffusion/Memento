namespace Memento.AI.Local;

/// <summary>Reads the RAM available now (a test seam for the worker's check before a larger model loads).</summary>
public interface ISystemMemoryProbe
{
    /// <summary>Available physical memory in bytes, or <c>null</c> when it cannot be read.</summary>
    long? AvailableBytes();

    /// <summary>This process's working set in bytes (what the model takes in RAM once loaded).</summary>
    long WorkingSetBytes();
}
