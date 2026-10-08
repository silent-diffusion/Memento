namespace Memento.Core.Engines;

/// <summary>One instance of <c>\GPU Process Memory(*)\Dedicated Usage</c>: its name (<c>pid_N_luid_0x…_0x…_phys_N</c>) and value.</summary>
public readonly record struct GpuProcessCounter(string Instance, long Bytes);
