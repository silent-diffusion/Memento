namespace Memento.Core.Engines;

/// <summary>A running process as the GPU memory attribution needs it.</summary>
/// <param name="ExeName">The executable's file name, e.g. <c>llama-server.exe</c>.</param>
/// <param name="ParentPid">The process that started it (0 when unknown); Windows reuses ids, so check <paramref name="StartedAt"/>.</param>
/// <param name="FriendlyName">The executable's file description ("Python"), or <c>null</c>.</param>
/// <param name="StartedAt">When it started, or <c>null</c> when Windows does not say.</param>
public sealed record ProcessEntry(int Pid, int ParentPid, string ExeName, string? FriendlyName, DateTime? StartedAt);
