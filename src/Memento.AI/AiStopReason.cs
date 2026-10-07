namespace Memento.AI;

/// <summary>Why generation stopped. Anything but <see cref="Completed"/> means the text may be incomplete.</summary>
public enum AiStopReason
{
    /// <summary>The model finished its answer.</summary>
    Completed,

    /// <summary>The output limit was reached: the answer is cut off and must not be parsed as complete.</summary>
    MaxTokens,

    /// <summary>The model or the provider's safety system declined the request.</summary>
    Refusal,

    /// <summary>The user cancelled; the text is whatever had arrived.</summary>
    Cancelled,

    /// <summary>The provider ended for another reason (reported in <see cref="AiResponse.ProviderStopReason"/>).</summary>
    Other,
}
