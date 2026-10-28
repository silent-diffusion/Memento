namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Payload of <c>ask.progress</c> (2.9.0): one step of an answer. The text is content on its way from the chosen provider;
/// the host keeps only the finished (or stopped) answer, in the conversation file.
/// </summary>
/// <param name="Kind">
/// <c>context</c> (what the answer is given, before anything is sent), <c>token</c> (text that arrived since the previous
/// token event), <c>done</c> (the answer, saved), <c>cancelled</c> (stopped; any text that arrived is saved as a stopped
/// answer) or <c>failed</c> (nothing saved; <see cref="Code"/> and the §17 message in <see cref="Text"/>). Nothing follows
/// <c>done</c>, <c>cancelled</c> or <c>failed</c>.
/// </param>
/// <param name="Text">With <c>token</c>: the new text; with <c>failed</c> and <c>cancelled</c>: what happened.</param>
public sealed record AskProgress(string JobId, string RecordingId, string Kind, string Text)
{
    /// <summary>With <c>context</c>.</summary>
    public AskContextInfo? Context { get; init; }

    /// <summary>With <c>done</c> (and <c>cancelled</c> when text had arrived): the saved answer.</summary>
    public AskTurn? Turn { get; init; }

    /// <summary>With <c>failed</c>: the error code (<c>ai.network</c>, <c>ai.serverUnreachable</c>…).</summary>
    public string? Code { get; init; }
}