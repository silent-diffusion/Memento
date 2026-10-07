namespace Memento.AI;

/// <summary>What a provider is doing right now.</summary>
public enum AiProgressStage
{
    /// <summary>Local: loading the model into memory.</summary>
    Loading,

    /// <summary>The request is being sent, or the local model is reading the prompt.</summary>
    Sending,

    /// <summary>Output is arriving.</summary>
    Generating,

    /// <summary>Waiting before a retry (rate limit or a temporary server error).</summary>
    WaitingToRetry,

    /// <summary>The answer is complete.</summary>
    Done,
}
