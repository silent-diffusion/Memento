namespace Memento.AI;

/// <summary>The stable codes of <see cref="AiError"/>; the bridge and the UI key their copy on these.</summary>
public static class AiErrorCodes
{
    /// <summary>No API key is saved for the provider.</summary>
    public const string NoKey = "ai.noKey";

    /// <summary>The provider rejected the key (HTTP 401) or does not allow it this request (403).</summary>
    public const string InvalidKey = "ai.invalidKey";

    /// <summary>The provider is limiting requests (HTTP 429); <see cref="AiError.RetryAfter"/> says when to try again.</summary>
    public const string RateLimited = "ai.rateLimited";

    /// <summary>The provider could not be reached, or did not answer in time.</summary>
    public const string Network = "ai.network";

    /// <summary>The provider failed on its side or sent something that could not be read.</summary>
    public const string ProviderError = "ai.providerError";

    /// <summary>The prompt and the output limit do not fit the model's context.</summary>
    public const string ContentTooLong = "ai.contentTooLong";

    /// <summary>The user cancelled. <see cref="IAiProvider.GenerateAsync"/> throws <see cref="OperationCanceledException"/>; this code is for copy and records.</summary>
    public const string Cancelled = "ai.cancelled";

    /// <summary>The local model file is not installed (or is incomplete).</summary>
    public const string ModelNotInstalled = "ai.modelNotInstalled";

    /// <summary>The graphics card does not have enough free memory, or allocations spilled to shared memory.</summary>
    public const string NotEnoughVram = "ai.notEnoughVram";

    /// <summary>The local model's worker process stopped without an answer.</summary>
    public const string WorkerCrashed = "ai.workerCrashed";

    public static IReadOnlyList<string> All { get; } =
        [NoKey, InvalidKey, RateLimited, Network, ProviderError, ContentTooLong, Cancelled, ModelNotInstalled, NotEnoughVram, WorkerCrashed];
}
