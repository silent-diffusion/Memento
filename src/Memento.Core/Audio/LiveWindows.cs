namespace Memento.Core.Audio;

/// <summary>
/// The live transcript's windows (2.0): 10 seconds each on the recording timeline (pauses excluded, as in the
/// transcript), heard in order once every open track's audio on disk covers the window. A draft never queues up work:
/// when it has fallen more than one window behind (a slow processor, a pause for a full pass), the windows in between
/// are skipped and the latest complete one is heard.
/// </summary>
public static class LiveWindows
{
    public const long WindowMs = 10_000;

    /// <summary>The window to hear next, or <c>null</c> while the next one is not complete on disk.</summary>
    /// <param name="next">The first window not heard or skipped yet.</param>
    /// <param name="coveredMs">How far every open track's audio reaches, on the recording timeline.</param>
    public static LiveWindow? Next(int next, long coveredMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(next);
        var complete = (int)Math.Max(0, coveredMs / WindowMs);
        if (complete <= next)
        {
            return null;
        }

        var index = complete - next > 1 ? complete - 1 : next;
        return new LiveWindow(index, index * WindowMs, (index + 1) * WindowMs, index - next);
    }

    /// <summary>
    /// How far every open track reaches: the least of their <paramref name="covered"/> ends, never past the recorded
    /// time; tracks that ended before the session (a source turned off or lost) do not hold the others back.
    /// </summary>
    public static long Covered(IEnumerable<(long CoveredUntilMs, bool Open)> covered, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(covered);
        var open = covered.Where(c => c.Open).Select(c => c.CoveredUntilMs).ToList();
        return open.Count == 0 ? 0 : Math.Min(elapsedMs, open.Min());
    }
}
