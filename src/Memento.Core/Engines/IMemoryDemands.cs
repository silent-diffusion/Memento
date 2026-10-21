namespace Memento.Core.Engines;

/// <summary>Reads <see cref="MemoryDemands"/> now; cheap, never throws.</summary>
public interface IMemoryDemands
{
    MemoryDemands Current();
}
