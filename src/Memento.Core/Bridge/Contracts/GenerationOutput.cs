namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Payload of <c>generation.output</c> (BRIDGE.md M4, Live output): one step of a generation's exchange with its
/// provider, for the Live output sheet. The text is content that is already on this PC or is sent to or received from the
/// chosen provider; the host keeps none of it after sending and never writes it to disk.
/// </summary>
/// <param name="PassId">The pass this event belongs to (<c>p1</c>, <c>p2</c>… in order); empty for <c>done</c>.</param>
/// <param name="Kind">
/// <c>step</c> (work done in code: segment, reduce, grounding), <c>request</c> (a pass starts: the exact text sent),
/// <c>token</c> (text that arrived since the previous token event of the pass), <c>reply</c> (the whole reply, with its
/// counts), <c>done</c> (the job's output has ended; nothing follows).
/// </param>
/// <param name="Step"><c>segment</c>, <c>map</c>, <c>reduce</c>, <c>verify</c> or <c>grounding</c>; <c>null</c> for <c>token</c>, <c>reply</c> and <c>done</c>.</param>
/// <param name="Title">With <c>step</c> and <c>request</c>: what the pass is ("Decisions and action items · segment 1 of 2").</param>
/// <param name="Text">See <paramref name="Kind"/>; with <c>step</c>, what the step did ("2 segments of up to 3,000 tokens").</param>
public sealed record GenerationOutput(string JobId, string PassId, string Kind, string? Step, string? Title, string Text)
{
    /// <summary>With <c>request</c>: the reply streams in (the local model) or arrives whole (a cloud provider).</summary>
    public bool? Streamed { get; init; }

    /// <summary>With <c>token</c>: output tokens so far; with <c>reply</c>: the reply's output tokens.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>With <c>reply</c>: the tokens of the request as the provider counted them.</summary>
    public int? PromptTokens { get; init; }

    /// <summary>With <c>token</c> and <c>reply</c> from the local model: output tokens per second.</summary>
    public double? TokensPerSecond { get; init; }

    /// <summary>With <c>step</c>: how long it took; with <c>token</c>: writing so far; with <c>reply</c>: from the request to the reply.</summary>
    public long? ElapsedMs { get; init; }

    /// <summary>With <c>reply</c>: the provider's stop reason (<c>eog</c>, <c>max_tokens</c>, <c>end_turn</c>…).</summary>
    public string? StopReason { get; init; }
}
