using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Text;

/// <summary>
/// The agenda parsers' regular expressions all carry <see cref="TimeoutMilliseconds"/>; a match that runs past it on
/// hostile text counts as no match (the line is then read as plain text) instead of tying up the host. Every match in
/// agenda parsing goes through here.
/// </summary>
internal static class RegexGuard
{
    /// <summary>The match timeout of every <c>[GeneratedRegex]</c> in agenda parsing.</summary>
    public const int TimeoutMilliseconds = 250;

    /// <summary>Lines longer than this are not run through the structural patterns (headings, tables, inline markup).</summary>
    public const int MaxLineLength = 4_000;

    private static readonly AsyncLocal<WorkTally?> CurrentTally = new();

    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> UntimedPatterns = new();

    /// <summary>The tally of the calling flow, if a test started one.</summary>
    internal static WorkTally? Tally => CurrentTally.Value;

    /// <summary>
    /// Starts a tally for the calling flow and the work it starts (tests only). The timeout is wall-clock time, so a
    /// test machine that stalls for longer than it cuts a linear match short; within a tally the patterns run without
    /// it (unless <paramref name="untimed"/> is false) and each match and parse counts the processor cycles it used, so
    /// the hardening tests check the work done on hostile text, which a stall cannot change.
    /// </summary>
    internal static WorkTally StartTally(bool untimed = true)
    {
        var tally = new WorkTally { Untimed = untimed };
        CurrentTally.Value = tally;
        return tally;
    }

    /// <summary>Records one match that ran past the timeout (also called by code that catches the timeout itself).</summary>
    internal static void NoteTimeout() => CurrentTally.Value?.NoteTimeout();

    /// <summary>The pattern a match runs: <paramref name="regex"/> itself, or within an untimed tally the same pattern compiled without a timeout.</summary>
    internal static Regex Use(Regex regex) =>
        CurrentTally.Value is { Untimed: true }
            ? UntimedPatterns.GetOrAdd((regex.ToString(), regex.Options), key => new Regex(key.Pattern, key.Options | RegexOptions.Compiled, Regex.InfiniteMatchTimeout))
            : regex;

    public static bool IsMatch(Regex regex, string input)
    {
        try
        {
            return Run(regex, input, string.Empty, static (r, s, _) => r.IsMatch(s));
        }
        catch (RegexMatchTimeoutException)
        {
            NoteTimeout();
            return false;
        }
    }

    public static Match Match(Regex regex, string input)
    {
        try
        {
            return Run(regex, input, string.Empty, static (r, s, _) => r.Match(s));
        }
        catch (RegexMatchTimeoutException)
        {
            NoteTimeout();
            return System.Text.RegularExpressions.Match.Empty;
        }
    }

    /// <summary>The matches of <paramref name="regex"/>, read eagerly; none when matching runs past the timeout.</summary>
    public static IReadOnlyList<Match> Matches(Regex regex, string input)
    {
        try
        {
            return Run(regex, input, string.Empty, static (r, s, _) => (IReadOnlyList<Match>)r.Matches(s).ToList());
        }
        catch (RegexMatchTimeoutException)
        {
            NoteTimeout();
            return [];
        }
    }

    /// <summary>
    /// <see cref="Regex.Replace(string, string)"/>; a timeout is left to the caller, which knows what the text should
    /// fall back to.
    /// </summary>
    public static string Replace(Regex regex, string input, string replacement) =>
        Run(regex, input, replacement, static (r, s, with) => r.Replace(s, with));

    /// <summary>
    /// Whether a match that ran past its timeout should run again: the timeout is wall-clock time, so on a starved
    /// machine (a busy recording and transcription, a stalled CI runner) a linear match can be stopped while it waits
    /// to run. When the thread did less work across the attempts than a 1 GHz processor does within the timeout, it was
    /// mostly waiting, not matching, and the match runs again (up to <see cref="MaxAttempts"/> times); a match on
    /// hostile text does that work within the first attempt on any real processor, and is given up as before.
    /// </summary>
    internal static bool StalledRatherThanSlow(TimeSpan timeout, long cyclesSpent, int attempts) =>
        attempts < MaxAttempts && cyclesSpent < (long)(timeout.TotalMilliseconds * CyclesPerMillisecondAt1GHz);

    /// <summary>How many times a match stopped by a stall runs at most.</summary>
    internal const int MaxAttempts = 8;

    private const double CyclesPerMillisecondAt1GHz = 1_000_000;

    private static T Run<T>(Regex regex, string input, string argument, Func<Regex, string, string, T> match)
    {
        var tally = CurrentTally.Value;
        var pattern = Use(regex);
        var spent = 0L;
        for (var attempt = 1; ; attempt++)
        {
            var start = ThreadCycles.Now();
            try
            {
                return match(pattern, input, argument);
            }
            catch (RegexMatchTimeoutException) when (StalledRatherThanSlow(pattern.MatchTimeout, spent + (ThreadCycles.Now() - start), attempt))
            {
                // Stopped while waiting to run, not while working: run it again.
            }
            finally
            {
                var cycles = ThreadCycles.Now() - start;
                spent += cycles;
                tally?.NoteMatch(cycles);
            }
        }
    }
}
