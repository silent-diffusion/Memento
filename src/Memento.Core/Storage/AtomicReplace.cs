using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Memento.Core.Storage.Interop;

namespace Memento.Core.Storage;

/// <summary>
/// The last step of an atomic write (CLAUDE.md): moves a finished, flushed <c>&lt;name&gt;.tmp</c> over the real file.
/// On Windows the replace uses POSIX semantics, so a reader that has the file open (the bridge reading
/// <c>project.json</c>, an indexer, a scanner) neither blocks the write nor sees a torn file: it keeps the old document
/// until it closes. Where that is unsupported (FAT, exFAT, some network shares, older Windows) it falls back to
/// <see cref="File.Move(string, string, bool)"/>. Either way, a reader that does not share delete is waited out for
/// about two seconds of waits between tries, with jitter, before the write fails; the waits are counted, not the clock,
/// so a machine too busy to wake the retry loop on time still gets every try.
/// </summary>
public static class AtomicReplace
{
    internal static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(2);

    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    private const int FirstDelayMs = 5;
    private const int LongestDelayMs = 100;

    /// <summary>Replaces <paramref name="destination"/> with <paramref name="source"/>, or creates it.</summary>
    /// <exception cref="IOException">Another program kept the file locked for the whole retry budget, or the move failed.</exception>
    /// <exception cref="OperationCanceledException">Cancelled while waiting to retry; the destination is unchanged.</exception>
    public static Task ReplaceAsync(string source, string destination, CancellationToken cancellationToken) =>
        ReplaceAsync(source, destination, OperatingSystem.IsWindows() ? PosixRename.Replace : null, DefaultBudget, cancellationToken);

    /// <param name="posixRename">Returns 0 or a Win32 error code; <c>null</c> to use <see cref="File.Move(string, string, bool)"/> only.</param>
    /// <param name="budget">How long to keep retrying a locked file, counted in the waits between tries (see below).</param>
    /// <param name="delay">Waits between tries (tests replace it to stand for a starved machine).</param>
    /// <remarks>
    /// The budget is the sum of the waits this method asks for, not the time on the clock. On a machine saturated by
    /// other work a 5 ms wait can come back after a second or more (the thread pool is starved), so a clock-based budget
    /// gave up after two or three tries and dropped a write that a brief lock (a scanner reading the new file) would
    /// have let through moments later. Counting the waits gives every write the same number of tries however slowly the
    /// machine runs; a lock that really stays costs about the budget on a normal machine, as before.
    /// </remarks>
    internal static async Task ReplaceAsync(
        string source,
        string destination,
        Func<string, string, int>? posixRename,
        TimeSpan budget,
        CancellationToken cancellationToken,
        Func<int, CancellationToken, Task>? delay = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        delay ??= static (ms, ct) => Task.Delay(ms, ct);
        var started = Stopwatch.GetTimestamp();
        var waited = TimeSpan.Zero;
        for (var attempt = 1; ; attempt++)
        {
            Exception failure;
            if (posixRename is not null)
            {
                var error = posixRename(source, destination);
                if (error == PosixRename.ErrorSuccess)
                {
                    return;
                }

                if (!IsTransient(error))
                {
                    // Unsupported here (ERROR_INVALID_PARAMETER, ERROR_NOT_SUPPORTED, ...) or a failure the managed
                    // move reports in its usual terms (a missing file, a bad path): let File.Move decide from now on.
                    posixRename = null;
                    attempt--;
                    continue;
                }

                failure = new Win32Exception(error);
            }
            else
            {
                try
                {
                    File.Move(source, destination, overwrite: true);
                    return;
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    failure = ex;
                }
            }

            if (waited >= budget)
            {
                throw GaveUp(destination, Stopwatch.GetElapsedTime(started), failure);
            }

            var wait = Math.Max(1, Math.Min(Delay(attempt), (int)Math.Ceiling((budget - waited).TotalMilliseconds)));
            waited += TimeSpan.FromMilliseconds(wait);
            await delay(wait, cancellationToken);
        }
    }

    private static bool IsTransient(int error) => error is ErrorAccessDenied or ErrorSharingViolation or ErrorLockViolation;

    private static bool IsTransient(Exception exception) =>
        exception is UnauthorizedAccessException
        || (exception is IOException and not FileNotFoundException and not DirectoryNotFoundException and not PathTooLongException
            && !Audio.DiskErrors.IsDiskFull(exception));

    /// <summary>Doubles from 5 ms up to 100 ms, each wait drawn from its upper half so retrying writers spread out.</summary>
    private static int Delay(int attempt)
    {
        var ceiling = Math.Min(LongestDelayMs, FirstDelayMs << Math.Min(attempt - 1, 8));
#pragma warning disable CA5394 // Jitter for retries, not security.
        return Random.Shared.Next(ceiling / 2, ceiling + 1);
#pragma warning restore CA5394
    }

    private static IOException GaveUp(string destination, TimeSpan elapsed, Exception failure)
    {
        var seconds = elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        var message =
            $"Memento could not replace {Path.GetFileName(destination)} because another program kept it open for {seconds} s. "
            + "The previous version of the file is unchanged. Close programs that may be scanning the folder (backup, sync or antivirus) and try again.";
        var hresult = failure is Win32Exception win32 ? unchecked((int)0x8007_0000) | win32.NativeErrorCode : failure.HResult;
        return new IOException(message, failure) { HResult = hresult };
    }
}
