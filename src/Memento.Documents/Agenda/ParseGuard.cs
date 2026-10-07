namespace Memento.Documents.Agenda;

/// <summary>
/// Runs a parser's work off the caller's thread and keeps the <see cref="IAgendaParser"/> contract: whatever the
/// libraries underneath throw on a damaged file (XML, ZIP, number and enum format errors, missing parts) becomes
/// <see cref="AgendaErrorCodes.Unreadable"/>; only <see cref="AgendaImportException"/> and a requested
/// <see cref="OperationCanceledException"/> reach the caller. <see cref="OutOfMemoryException"/> is never swallowed.
/// </summary>
internal static class ParseGuard
{
    /// <summary>Runs <paramref name="parse"/> on the thread pool; <paramref name="what"/> names the format in the error ("a Word document").</summary>
    public static Task<AgendaParseResult> RunAsync(
        AgendaParseOptions options,
        string what,
        Func<CancellationToken, AgendaParseResult> parse,
        CancellationToken cancellationToken) =>
        RunAsync(options, what, token => Task.FromResult(parse(token)), cancellationToken);

    /// <inheritdoc cref="RunAsync(AgendaParseOptions, string, Func{CancellationToken, AgendaParseResult}, CancellationToken)"/>
    public static async Task<AgendaParseResult> RunAsync(
        AgendaParseOptions options,
        string what,
        Func<CancellationToken, Task<AgendaParseResult>> parse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await Task.Run(() => parse(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (IsDamage(e, cancellationToken))
        {
            throw AgendaErrors.Unreadable(options, what, e);
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/> means the content could not be read, rather than a cancellation the caller
    /// asked for, an agenda error already worded for the user, or the process running out of memory.
    /// </summary>
    public static bool IsDamage(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        AgendaImportException or OutOfMemoryException => false,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => true,
    };
}
