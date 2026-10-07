namespace Memento.AI.Local;

/// <summary>Why a local generation stopped (<see cref="LocalLlmOutput.StopReason"/>).</summary>
public static class LocalLlmStopReasons
{
    /// <summary>The model produced its end-of-generation token.</summary>
    public const string EndOfGeneration = "eog";

    /// <summary>The prompt's token limit was reached; the text is cut off.</summary>
    public const string MaxTokens = "max_tokens";

    /// <summary>Prompt plus output limit do not fit the context; nothing was generated.</summary>
    public const string ContextFull = "context";

    public const string Cancelled = "cancelled";

    public static AiStopReason ToAi(string reason) => reason switch
    {
        EndOfGeneration => AiStopReason.Completed,
        MaxTokens => AiStopReason.MaxTokens,
        Cancelled => AiStopReason.Cancelled,
        _ => AiStopReason.Other,
    };
}
