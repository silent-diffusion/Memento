namespace Memento.Core.Bridge.Contracts;

/// <summary>A program holding video memory on the graphics card (<see cref="GpuMemoryInfo.Holders"/>).</summary>
/// <param name="ProcessName">The executable holding most of it, e.g. <c>llama-server.exe</c>.</param>
/// <param name="Description">"Ollama (llama-server.exe)", "python.exe (started by Dictation)", "Windows desktop (dwm.exe)", "Memento".</param>
/// <param name="Bytes">Dedicated video memory it holds.</param>
/// <param name="Memento">Memento's own processes (the app, its WebView2 processes, its worker), counted as one entry.</param>
/// <param name="StartedBy">The app that started a runtime such as Python or Ollama's server, or <c>null</c>.</param>
public sealed record GpuMemoryHolderInfo(string ProcessName, string Description, long Bytes, bool Memento, string? StartedBy);
