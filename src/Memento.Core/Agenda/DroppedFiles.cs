namespace Memento.Core.Agendas;

/// <summary>
/// File paths of the last drop onto the page. JavaScript only sees file names, so the host records the real paths
/// WebView2 hands it with the drop message (<c>postMessageWithAdditionalObjects</c>), and <c>agenda.importDropped</c>
/// matches the names the UI sends. A drop is remembered for <see cref="Lifetime"/>.
/// </summary>
public sealed class DroppedFiles(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private IReadOnlyList<string> _paths = [];
    private DateTimeOffset _at;

    /// <summary>Records the paths of one drop, replacing the previous one.</summary>
    public void Register(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p)).ToList();
        lock (_gate)
        {
            _paths = list;
            _at = time.GetUtcNow();
        }
    }

    /// <summary>The first recorded path whose file name is one of <paramref name="names"/> (or a full path that was recorded), or <c>null</c>.</summary>
    public string? Resolve(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        IReadOnlyList<string> paths;
        lock (_gate)
        {
            if (time.GetUtcNow() - _at > Lifetime)
            {
                return null;
            }

            paths = _paths;
        }

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var match = paths.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase))
                ?? paths.FirstOrDefault(p => string.Equals(Path.GetFileName(p), Path.GetFileName(name), StringComparison.OrdinalIgnoreCase));
            if (match is not null && File.Exists(match))
            {
                return match;
            }
        }

        return null;
    }
}
