namespace Memento.Core.Engines;

/// <summary>Reads GPUs and free video memory, processor load and memory. Cheap enough to call every few seconds.</summary>
public interface IResourceProbe
{
    ResourceSnapshot Sample();

    /// <summary>Like <see cref="Sample"/>, but nothing read earlier is reused (who holds the card's memory is cached for a few seconds).</summary>
    ResourceSnapshot Refresh() => Sample();
}
