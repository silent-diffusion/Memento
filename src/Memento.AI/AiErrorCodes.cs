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

    /// <summary>The provider does not offer the model chosen in Settings (HTTP 404, or 400 naming the model).</summary>
    public const string UnknownModel = "ai.unknownModel";

    /// <summary>The user cancelled. <see cref="IAiProvider.GenerateAsync"/> throws <see cref="OperationCanceledException"/>; this code is for copy and records.</summary>
    public const string Cancelled = "ai.cancelled";

    /// <summary>The local model file is not installed (or is incomplete).</summary>
    public const string ModelNotInstalled = "ai.modelNotInstalled";

    /// <summary>The graphics card does not have enough free memory, or allocations spilled to shared memory.</summary>
    public const string NotEnoughVram = "ai.notEnoughVram";

    /// <summary>
    /// A larger local model does not fit in RAM and video memory with what Windows, the app and a recording need kept free,
    /// or it was unloaded because a recording started and capture would have been short of memory.
    /// </summary>
    public const string NotEnoughMemory = "ai.notEnoughMemory";

    /// <summary>The local model's worker process stopped without an answer.</summary>
    public const string WorkerCrashed = "ai.workerCrashed";

    /// <summary>"Your own server": nothing answered at the address (refused, unreachable, not found, no answer in time).</summary>
    public const string ServerUnreachable = "ai.serverUnreachable";

    /// <summary>"Your own server": something answered, but not like an OpenAI-compatible server.</summary>
    public const string ServerNotCompatible = "ai.serverNotCompatible";

    /// <summary>"Your own server": HTTP 401 or 403, a key is missing or wrong.</summary>
    public const string ServerNeedsKey = "ai.serverNeedsKey";

    /// <summary>"Your own server": it does not offer the model chosen in Settings.</summary>
    public const string ServerModelUnknown = "ai.serverModelUnknown";

    /// <summary>"Your own server": a plain <c>http://</c> address outside this PC and the local network.</summary>
    public const string ServerInsecure = "ai.serverInsecure";

    public static IReadOnlyList<string> All { get; } =
        [NoKey, InvalidKey, RateLimited, Network, ProviderError, ContentTooLong, UnknownModel, Cancelled, ModelNotInstalled, NotEnoughVram, NotEnoughMemory, WorkerCrashed,
         ServerUnreachable, ServerNotCompatible, ServerNeedsKey, ServerModelUnknown, ServerInsecure];
}
