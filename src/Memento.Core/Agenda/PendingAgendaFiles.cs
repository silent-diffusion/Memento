using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Memento.Core.Agendas;

/// <summary>
/// The attachment tokens of <c>AgendaParsePreview</c>: a private copy of each imported file, so what is attached is
/// exactly what was parsed even if the original changes in the meantime. Copies expire after
/// <see cref="PendingAgendaOptions.Expiry"/> and are all removed when Memento closes.
/// </summary>
public sealed class PendingAgendaFiles(PendingAgendaOptions options, TimeProvider time) : IDisposable
{
    private readonly ConcurrentDictionary<string, PendingAgendaFile> _held = new(StringComparer.Ordinal);
    private int _sweptAbandoned;

    public int Count => _held.Count;

    /// <summary>Copies <paramref name="sourcePath"/> and returns its token.</summary>
    public async Task<string> HoldAsync(string sourcePath, string? recordingId, CancellationToken cancellationToken)
    {
        Sweep();
        SweepAbandonedOnce();
        Directory.CreateDirectory(options.Folder);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var copy = Path.Combine(options.Folder, token + ".bin");
        try
        {
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            await using var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
        }
        catch
        {
            // A copy cut short never stays behind in %TEMP%.
            TryDelete(copy);
            throw;
        }

        await Attachments.MarkOfTheWeb.CopyAsync(sourcePath, copy, cancellationToken);
        _held[token] = new PendingAgendaFile(token, recordingId, copy, Path.GetFileName(sourcePath), time.GetUtcNow());
        return token;
    }

    /// <summary>The held file for <paramref name="token"/>, or <c>null</c> when it expired, was discarded or never existed.</summary>
    public PendingAgendaFile? Find(string token)
    {
        Sweep();
        return _held.TryGetValue(token, out var file) ? file : null;
    }

    /// <summary>Drops a held file (after it was attached, or on <c>agenda.discard</c>); <c>false</c> when there was none.</summary>
    public bool Release(string token)
    {
        if (!_held.TryRemove(token, out var file))
        {
            return false;
        }

        TryDelete(file.Path);
        return true;
    }

    public void Dispose()
    {
        foreach (var token in _held.Keys.ToList())
        {
            Release(token);
        }

        try
        {
            if (Directory.Exists(options.Folder))
            {
                Directory.Delete(options.Folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder; Windows cleans it up eventually.
        }
    }

    /// <summary>
    /// Copies held by a Memento that crashed stay in their own <c>&lt;pid&gt;</c> folder next to this one; they are agenda
    /// files (possibly confidential) in %TEMP%, so the first hold of a session removes the folders of processes that
    /// are no longer running.
    /// </summary>
    private void SweepAbandonedOnce()
    {
        // Only ever inside Memento's own agenda-pending folder, never next to a folder chosen some other way.
        if (Interlocked.Exchange(ref _sweptAbandoned, 1) == 1
            || Path.GetDirectoryName(Path.GetFullPath(options.Folder)) is not { } parent
            || !string.Equals(Path.GetFileName(parent), PendingAgendaOptions.PendingFolderName, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(parent))
        {
            return;
        }

        var own = Path.GetFileName(Path.GetFullPath(options.Folder));
        foreach (var folder in Directory.EnumerateDirectories(parent))
        {
            var name = Path.GetFileName(folder);
            if (string.Equals(name, own, StringComparison.OrdinalIgnoreCase) || !int.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid) || IsRunning(pid))
            {
                continue;
            }

            try
            {
                Projects.LinkSafeFiles.DeleteTree(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still in use or protected; the next session tries again.
            }
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void Sweep()
    {
        var now = time.GetUtcNow();
        foreach (var (token, file) in _held)
        {
            if (now - file.HeldAt > options.Expiry)
            {
                Release(token);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in the temp folder; removed with it when Memento closes.
        }
    }
}
