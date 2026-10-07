namespace Memento.Core.Engines;

/// <summary>Reads GPUs and free video memory, processor load and memory. Cheap enough to call every few seconds.</summary>
public interface IResourceProbe
{
    ResourceSnapshot Sample();
}
