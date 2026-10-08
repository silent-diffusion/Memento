using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Memento.Core;

namespace Memento.App;

/// <summary>
/// One Memento per data folder in a Windows session (in practice one per user, since the data folder is always
/// <c>%LOCALAPPDATA%\Memento</c>). The first instance owns a named mutex and waits on a named event; a second
/// instance sets the event (after allowing the first to take the foreground) and exits. The names carry a hash of the
/// data folder, so a test run with its own LOCALAPPDATA never hands off to, or blocks, the Memento a person is using.
/// </summary>
internal sealed partial class SingleInstanceGuard : IDisposable
{
    private const int AsfwAny = -1;
    private static readonly string MutexName = @"Local\Memento.SingleInstance." + DataRootKey();
    private static readonly string ActivateEventName = @"Local\Memento.Activate." + DataRootKey();

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activate;
    private RegisteredWaitHandle? _registration;

    private SingleInstanceGuard(Mutex mutex, bool isFirstInstance)
    {
        _mutex = mutex;
        IsFirstInstance = isFirstInstance;
        if (isFirstInstance)
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        }
    }

    public bool IsFirstInstance { get; }

    /// <summary>Takes the single-instance mutex if no other Memento holds it.</summary>
    public static SingleInstanceGuard Acquire()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous instance ended without releasing it (crash); the mutex is now ours.
            owned = true;
        }

        return new SingleInstanceGuard(mutex, owned);
    }

    /// <summary>First instance: runs <paramref name="onActivate"/> (on a pool thread) whenever a second instance starts.</summary>
    public void ListenForActivation(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);
        if (_activate is null)
        {
            throw new InvalidOperationException("Only the first instance listens for activation.");
        }

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Second instance: asks the first to bring its window forward.</summary>
    /// <returns><c>false</c> if the first instance is not listening yet (it is still starting).</returns>
    public static bool SignalFirstInstance()
    {
        // The process the user just launched may hand the foreground to the running window.
        AllowSetForegroundWindow(AsfwAny);
        if (!EventWaitHandle.TryOpenExisting(ActivateEventName, out var activate))
        {
            return false;
        }

        using (activate)
        {
            return activate.Set();
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activate?.Dispose();
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    /// <summary>16 hex digits of the SHA-256 of the full data folder path, ignoring case.</summary>
    private static string DataRootKey()
    {
        var root = Path.GetFullPath(AppPaths.DataRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16];
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
