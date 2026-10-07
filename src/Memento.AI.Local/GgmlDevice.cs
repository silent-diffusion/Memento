namespace Memento.AI.Local;

/// <summary>A ggml backend device (<c>Vulkan0</c>, <c>CPU</c>) with the memory it reports free right now.</summary>
/// <param name="Type"><c>CPU</c>, <c>GPU</c> (discrete), <c>IGPU</c> or <c>ACCEL</c>.</param>
public sealed record GgmlDevice(int Index, string Name, string Description, string Type, long FreeBytes, long TotalBytes);
