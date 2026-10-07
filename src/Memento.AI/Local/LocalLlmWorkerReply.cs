namespace Memento.AI.Local;

/// <summary>
/// A line from the worker to the host, with Core's message types (<c>ready</c>, <c>device</c>, <c>progress</c>,
/// then exactly one of <c>result</c>, <c>error</c> or <c>cancelled</c>; <c>log</c> anywhere). The LLM bodies sit in
/// their own properties so the integration can add them to Core's <c>WorkerReply</c> unchanged.
/// </summary>
public sealed record LocalLlmWorkerReply
{
    public required string Type { get; init; }

    public int? Pid { get; init; }

    public double? Percent { get; init; }

    public LocalLlmDeviceInfo? LlmDevice { get; init; }

    public LocalLlmProgress? LlmProgress { get; init; }

    public LocalLlmResult? Llm { get; init; }

    /// <summary>With <c>error</c>: one of <see cref="AiErrorCodes"/> (or Core's <c>invalidJob</c>).</summary>
    public string? Code { get; init; }

    /// <summary>With <c>error</c>: the user-facing copy; with <c>log</c>: the line (never content).</summary>
    public string? Message { get; init; }

    public string? Level { get; init; }
}
