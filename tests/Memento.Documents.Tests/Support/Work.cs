using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Tests.Support;

/// <summary>
/// Processor work, not elapsed time, for the parser hardening tests. A stalled runner stretches elapsed time without
/// limit, which made stopwatch and match-timeout checks fail on CI, but it cannot change the processor cycles a thread
/// uses (<see cref="ThreadCycles"/>). The work is done inside a <see cref="RegexGuard.StartTally"/>, so no pattern is cut
/// short by its wall-clock timeout either. The regex and parse budgets are the product's own limits on a 1 GHz processor
/// (several times slower than any machine these tests run on, and far below what the regressions they guard against
/// cost); the old stopwatch limits of the fuzz and pathological-input tests are converted by <see cref="Budget"/>.
/// Cycles are counted by the processor's constant-rate timestamp counter, so they do not depend on its current speed.
/// </summary>
internal static class Work
{
    /// <summary>The cycles of one millisecond on a 1 GHz processor.</summary>
    public const long CyclesPerMillisecond = 1_000_000;

    /// <summary>The work one match may do: its timeout on a 1 GHz processor.</summary>
    public const long MatchBudget = RegexGuard.TimeoutMilliseconds * CyclesPerMillisecond;

    /// <summary>The work a whole parse may do: the product's parse ceiling (<see cref="AgendaLimits.ParseTimeout"/>) on a 1 GHz processor.</summary>
    public static long ParseBudget => (long)AgendaLimits.ParseTimeout.TotalMilliseconds * CyclesPerMillisecond;

    /// <summary>Parse options without the wall-clock ceiling: the work is checked against <see cref="ParseBudget"/> instead.</summary>
    public static AgendaParseOptions Unlimited => AgendaParseOptions.Default with { ParseTimeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// A stopwatch limit of the fuzz and pathological-input tests as work: the nominal seconds times 3 (the time scale
    /// those limits were run with) at a 3 GHz reference clock. That keeps their sensitivity (the regressions they guard
    /// against cost minutes) without a stalled runner failing them. The slowest of them does about 11 G cycles.
    /// </summary>
    public static long Budget(double nominalSeconds) => (long)(nominalSeconds * 3 * 3_000 * CyclesPerMillisecond);

    /// <summary>
    /// The fewest cycles <paramref name="action"/> used in <paramref name="runs"/> runs, after one run that compiles the
    /// patterns (unless <paramref name="warmUp"/> is off, for inputs heavy enough that compiling is noise). It runs on a
    /// thread of its own inside an untimed tally, so the tally ends with it.
    /// </summary>
    public static long Of(Action action, int runs = 3, bool warmUp = true)
    {
        var least = long.MaxValue;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RegexGuard.StartTally();
                if (warmUp)
                {
                    action();
                }

                for (var i = 0; i < runs; i++)
                {
                    var start = ThreadCycles.Now();
                    action();
                    least = Math.Min(least, ThreadCycles.Now() - start);
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("The measured work failed.", failure);
        }

        return least;
    }

    /// <summary>
    /// The cycles one run used, inside an untimed tally: its synchronous part on the calling thread plus the parses it
    /// started (<c>ParseGuard</c> counts each on the thread it ran on), and what it threw, if anything. Work a parse hands
    /// to other threads after its first await (a PDF page rendered by Windows) is not counted.
    /// </summary>
    public static async Task<(long Cycles, Exception? Error)> OfRunAsync(Func<Task> run)
    {
        var tally = RegexGuard.StartTally();
        Exception? error = null;
        var start = ThreadCycles.Now();
        Task task;
        try
        {
            task = run();
        }
        catch (Exception e)
        {
            task = Task.FromException(e);
        }

        var synchronous = ThreadCycles.Now() - start;
        try
        {
            await task;
        }
        catch (Exception e)
        {
            error = e;
        }

        return (synchronous + tally.ParseCycles, error);
    }

    /// <summary><see cref="OfRunAsync"/> for a parse that must succeed: its result and the cycles it used.</summary>
    public static async Task<(T Result, long Cycles)> OfAsync<T>(Func<Task<T>> parse)
    {
        T result = default!;
        var (cycles, error) = await OfRunAsync(async () => result = await parse());
        if (error is not null)
        {
            throw new InvalidOperationException("The measured parse failed.", error);
        }

        return (result, cycles);
    }

    /// <summary>
    /// The fewest cycles a parse used, and the fewest its costliest match used, in <paramref name="runs"/> runs after one
    /// run that compiles the patterns. A parse may refuse the text (<see cref="AgendaImportException"/>); that is work
    /// done too.
    /// </summary>
    public static async Task<(long Parse, long LongestMatch)> OfParseAsync(Func<Task> parse, int runs = 2)
    {
        var least = long.MaxValue;
        var longestMatch = long.MaxValue;
        for (var i = 0; i <= runs; i++)
        {
            var tally = RegexGuard.StartTally();
            try
            {
                await parse();
            }
            catch (AgendaImportException)
            {
                // Refusing is fine: only the work counts.
            }

            Assert.Equal(0, tally.Timeouts);
            if (i > 0)
            {
                least = Math.Min(least, tally.ParseCycles);
                longestMatch = Math.Min(longestMatch, tally.LongestMatchCycles);
            }
        }

        return (least, longestMatch);
    }
}
