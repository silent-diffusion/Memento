using System.Globalization;

namespace Memento.Core.Engines;

/// <summary>
/// Who holds a graphics card's memory: the per-process counters (<c>\GPU Process Memory(pid_N_luid_…_phys_N)\Dedicated
/// Usage</c>) summed by process for one adapter, named the way the owner would recognise them. Ollama's model server is
/// "Ollama (llama-server.exe)"; a runtime started by an app (Python, Node, llama.cpp) also names that app ("started by
/// …"); every process of Memento itself (the app, its WebView2 processes, its worker) is one entry, "Memento". Pure
/// functions over counter data and a process snapshot, so they are tested without a graphics card.
/// </summary>
public static class GpuMemoryAttribution
{
    /// <summary>How many holders a snapshot keeps.</summary>
    public const int MaxHolders = 3;

    /// <summary>Below this a program is not worth naming (every windowed app holds a few megabytes for its surfaces).</summary>
    public const long MinimumHolderBytes = 32L * 1024 * 1024;

    /// <summary>What Memento's own processes are called.</summary>
    public const string MementoName = "Memento";

    /// <summary>Processes looked up per attribution: the largest few are all that can make the list.</summary>
    private const int DescribedProcesses = 12;

    /// <summary>How far up the parent chain a launcher or Memento is looked for.</summary>
    private const int MaxAncestors = 6;

    private static readonly HashSet<string> OllamaFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        "ollama.exe", "ollama app.exe", "ollama_llama_server.exe",
    };

    private static readonly HashSet<string> MementoExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Memento.exe", "Memento.App.exe", "Memento.Worker.exe",
    };

    /// <summary>Runtimes that do another program's work: the program that started them is named too.</summary>
    private static readonly HashSet<string> Runtimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ollama.exe", "ollama app.exe", "ollama_llama_server.exe", "llama-server.exe",
        "python.exe", "pythonw.exe", "py.exe", "node.exe", "java.exe", "javaw.exe", "dotnet.exe",
    };

    /// <summary>Shells and Windows processes that start programs for the owner; never named as "started by".</summary>
    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "svchost.exe", "services.exe", "wininit.exe", "winlogon.exe", "userinit.exe", "sihost.exe",
        "taskhostw.exe", "runtimebroker.exe", "smss.exe", "csrss.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "conhost.exe", "windowsterminal.exe", "openconsole.exe", "bash.exe", "wsl.exe", "wslhost.exe", "startmenuexperiencehost.exe",
    };

    /// <summary>Parts of Windows that hold some video memory for the desktop; never offered as something to close.</summary>
    private static readonly HashSet<string> WindowsParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm.exe", "explorer.exe", "csrss.exe", "searchhost.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
        "shellhost.exe", "textinputhost.exe", "lockapp.exe",
    };

    /// <summary>The adapter LUID as one number: the high part in the upper 32 bits.</summary>
    public static long Luid(uint lowPart, int highPart) => ((long)highPart << 32) | lowPart;

    /// <summary>Reads <c>pid_5192_luid_0x00000000_0x0000F961_phys_0</c>.</summary>
    public static bool TryParseInstance(string instance, out int pid, out long luid)
    {
        pid = 0;
        luid = 0;
        if (string.IsNullOrEmpty(instance))
        {
            return false;
        }

        var parts = instance.Split('_');
        if (parts.Length < 5
            || !parts[0].Equals("pid", StringComparison.OrdinalIgnoreCase)
            || !parts[2].Equals("luid", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out pid)
            || !TryHex(parts[3], out var high)
            || !TryHex(parts[4], out var low))
        {
            pid = 0;
            return false;
        }

        luid = Luid(low, unchecked((int)high));
        return true;
    }

    /// <summary>Dedicated bytes per process on the adapter <paramref name="luid"/>, summed over its physical adapters.</summary>
    public static IReadOnlyDictionary<int, long> ByProcess(IEnumerable<GpuProcessCounter> counters, long luid)
    {
        ArgumentNullException.ThrowIfNull(counters);
        var result = new Dictionary<int, long>();
        foreach (var counter in counters)
        {
            if (counter.Bytes > 0 && TryParseInstance(counter.Instance, out var pid, out var instanceLuid) && instanceLuid == luid)
            {
                result[pid] = result.GetValueOrDefault(pid) + counter.Bytes;
            }
        }

        return result;
    }

    /// <summary>
    /// The programs holding the most memory, largest first: processes are described, Memento's own merged into one entry
    /// and processes with the same description (two Ollama servers) added together. Entries under
    /// <see cref="MinimumHolderBytes"/> are left out.
    /// </summary>
    public static IReadOnlyList<GpuMemoryHolder> Holders(IReadOnlyDictionary<int, long> byProcess, IProcessDirectory processes, int selfPid, int count = MaxHolders)
    {
        ArgumentNullException.ThrowIfNull(byProcess);
        ArgumentNullException.ThrowIfNull(processes);
        var groups = new Dictionary<string, (GpuMemoryHolder Holder, long Largest)>(StringComparer.Ordinal);
        foreach (var (pid, bytes) in byProcess.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(DescribedProcesses))
        {
            var entry = processes.Find(pid);
            var described = entry is null
                ? pid == selfPid
                    ? new GpuMemoryHolder(string.Create(CultureInfo.InvariantCulture, $"process {pid}"), MementoName, 0) { IsMemento = true }
                    : new GpuMemoryHolder(string.Create(CultureInfo.InvariantCulture, $"process {pid}"), string.Create(CultureInfo.InvariantCulture, $"Another program (process {pid})"), 0)
                : Describe(entry, processes, selfPid);
            var key = described.Description;
            groups[key] = groups.TryGetValue(key, out var group)
                ? (group.Holder with { ProcessName = bytes > group.Largest ? described.ProcessName : group.Holder.ProcessName, Bytes = group.Holder.Bytes + bytes }, Math.Max(bytes, group.Largest))
                : (described with { Bytes = bytes }, bytes);
        }

        return groups.Values
            .Select(g => g.Holder)
            .Where(h => h.Bytes >= MinimumHolderBytes)
            .OrderByDescending(h => h.Bytes)
            .ThenBy(h => h.Description, StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }

    /// <summary>The executable, how to name it, the app that started it, and whether it is part of Memento (<see cref="GpuMemoryHolder.Bytes"/> is 0).</summary>
    public static GpuMemoryHolder Describe(ProcessEntry entry, IProcessDirectory processes, int selfPid)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(processes);
        var exe = entry.ExeName;
        var ancestors = Ancestors(entry, processes).ToList();
        if (entry.Pid == selfPid || MementoExecutables.Contains(exe) || ancestors.Any(a => a.Pid == selfPid || MementoExecutables.Contains(a.ExeName)))
        {
            return new GpuMemoryHolder(exe, MementoName, 0) { IsMemento = true };
        }

        var name = KnownName(entry, ancestors) ?? Friendly(entry);
        var launcher = Runtimes.Contains(exe) ? Launcher(ancestors) : null;
        var inBrackets = launcher is null ? exe : $"{exe}, started by {launcher}";
        var description = name is null ? (launcher is null ? exe : $"{exe} (started by {launcher})") : $"{name} ({inBrackets})";
        return new GpuMemoryHolder(exe, description, 0)
        {
            StartedBy = launcher,
            IsWindows = WindowsParts.Contains(exe),
        };
    }

    private static string? KnownName(ProcessEntry entry, List<ProcessEntry> ancestors)
    {
        if (OllamaFamily.Contains(entry.ExeName))
        {
            return "Ollama";
        }

        if (entry.ExeName.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Ollama runs its models in llama.cpp's server; other apps ship the same server on its own.
            return ancestors.Any(a => OllamaFamily.Contains(a.ExeName)) ? "Ollama" : "llama.cpp server";
        }

        return entry.ExeName.Equals("dwm.exe", StringComparison.OrdinalIgnoreCase) ? "Windows desktop" : null;
    }

    /// <summary>The file description when it says more than the file name ("Python" for python.exe), else <c>null</c>.</summary>
    private static string? Friendly(ProcessEntry entry)
    {
        var description = entry.FriendlyName?.Trim();
        var stem = Path.GetFileNameWithoutExtension(entry.ExeName);
        return string.IsNullOrEmpty(description) || description.Length > 40
            || description.Equals(stem, StringComparison.Ordinal)
            || description.Equals(entry.ExeName, StringComparison.OrdinalIgnoreCase)
            ? null
            : description;
    }

    /// <summary>The first ancestor that is neither a runtime nor a shell: the app that started the runtime.</summary>
    private static string? Launcher(List<ProcessEntry> ancestors)
    {
        foreach (var ancestor in ancestors)
        {
            if (Shells.Contains(ancestor.ExeName))
            {
                return null;
            }

            if (!Runtimes.Contains(ancestor.ExeName))
            {
                return Friendly(ancestor) ?? Path.GetFileNameWithoutExtension(ancestor.ExeName);
            }
        }

        return null;
    }

    /// <summary>Parents, nearest first; stops where Windows has reused a parent's id (the "parent" started later).</summary>
    private static IEnumerable<ProcessEntry> Ancestors(ProcessEntry entry, IProcessDirectory processes)
    {
        var child = entry;
        var seen = new HashSet<int> { entry.Pid };
        for (var i = 0; i < MaxAncestors && child.ParentPid > 0 && seen.Add(child.ParentPid); i++)
        {
            var parent = processes.Find(child.ParentPid);
            if (parent is null || (parent.StartedAt is { } p && child.StartedAt is { } c && p > c))
            {
                yield break;
            }

            yield return parent;
            child = parent;
        }
    }

    private static bool TryHex(string text, out uint value)
    {
        value = 0;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}
