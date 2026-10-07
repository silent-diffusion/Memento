namespace Memento.AI.Local;

/// <summary>Reads free video memory just before a load (ENGINE-NOTES.md section H, trap 2: read it, do not assume it).</summary>
public interface IGpuVramProbe
{
    /// <summary>The discrete graphics card, or <c>null</c> when there is none or it cannot be read.</summary>
    GpuVramReading? Read();
}
