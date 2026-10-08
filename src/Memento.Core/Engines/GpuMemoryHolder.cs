namespace Memento.Core.Engines;

/// <summary>
/// A program holding video memory on a graphics card, from the per-process counters Task Manager shows
/// (<c>\GPU Process Memory(pid_N_luid_…)\Dedicated Usage</c>), summed over its processes.
/// </summary>
/// <param name="ProcessName">The executable holding most of it, e.g. <c>llama-server.exe</c>.</param>
/// <param name="Description">How to name it to the owner: "Ollama (llama-server.exe)", "Python (python.exe, started by Dictation)", "Memento".</param>
/// <param name="Bytes">Dedicated video memory it holds on the card.</param>
public sealed record GpuMemoryHolder(string ProcessName, string Description, long Bytes)
{
    /// <summary>Memento itself: the app, its WebView2 processes or its worker.</summary>
    public bool IsMemento { get; init; }

    /// <summary>A part of Windows (the desktop, Explorer): named, never offered as something to close.</summary>
    public bool IsWindows { get; init; }

    /// <summary>The app that started this runtime (Python, Node, Ollama's server), when it is not a shell; <c>null</c> otherwise.</summary>
    public string? StartedBy { get; init; }
}
