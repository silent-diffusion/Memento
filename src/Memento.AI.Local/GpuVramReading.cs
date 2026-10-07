namespace Memento.AI.Local;

/// <summary>Free and total memory on the graphics card the model would use.</summary>
public sealed record GpuVramReading(string Name, long FreeBytes, long TotalBytes);
