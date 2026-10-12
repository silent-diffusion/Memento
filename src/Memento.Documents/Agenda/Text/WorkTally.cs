namespace Memento.Documents.Agenda.Text;

/// <summary>
/// What the agenda parsers did in one test flow (<see cref="RegexGuard.StartTally"/>): the processor cycles of the
/// costliest match and of the parses, and the matches that ran past the timeout. Lets the hardening tests measure work
/// instead of time.
/// </summary>
internal sealed class WorkTally
{
    private int _timeouts;
    private long _longestMatchCycles;
    private long _parseCycles;

    /// <summary>
    /// Whether the patterns run without their match timeout. On by default in a tally, so a test machine that stalls
    /// for longer than the timeout cannot cut a match short; the test of the timeout itself turns it off.
    /// </summary>
    public bool Untimed { get; init; } = true;

    /// <summary>The matches that ran past the timeout (only possible when <see cref="Untimed"/> is off).</summary>
    public int Timeouts => Volatile.Read(ref _timeouts);

    /// <summary>The processor cycles of the costliest single match (or replace) in this flow.</summary>
    public long LongestMatchCycles => Interlocked.Read(ref _longestMatchCycles);

    /// <summary>The processor cycles used by the parses started in this flow, on whichever thread each ran.</summary>
    public long ParseCycles => Interlocked.Read(ref _parseCycles);

    internal void NoteTimeout() => Interlocked.Increment(ref _timeouts);

    internal void NoteMatch(long cycles)
    {
        var longest = Interlocked.Read(ref _longestMatchCycles);
        while (cycles > longest)
        {
            var seen = Interlocked.CompareExchange(ref _longestMatchCycles, cycles, longest);
            if (seen == longest)
            {
                return;
            }

            longest = seen;
        }
    }

    internal void AddParseCycles(long cycles) => Interlocked.Add(ref _parseCycles, cycles);
}
