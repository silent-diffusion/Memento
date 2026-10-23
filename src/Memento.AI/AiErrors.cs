using System.Globalization;

namespace Memento.AI;

/// <summary>
/// Builds every <see cref="AiError"/> with its user-facing copy (DESIGN.md section 17, PRODUCT-SPEC "Errors and
/// Recovery": the provider and the cause, that nothing was sent twice, that no document was changed, then the fix).
/// </summary>
public static class AiErrors
{
    private const string CloudSafe = "Nothing was sent twice and no document was changed.";
    private const string LocalSafe = "Nothing left this PC and no document was changed.";

    public static AiError NoKey(string provider) => new(
        AiErrorCodes.NoKey,
        provider,
        $"{provider} has no API key saved, so nothing was sent and no document was changed. Add the key in Settings › AI and privacy.");

    public static AiError InvalidKey(string provider, int? status) => new(
        AiErrorCodes.InvalidKey,
        provider,
        $"{provider} did not accept the saved API key. {CloudSafe} Check or replace the key in Settings › AI and privacy.",
        HttpStatus: status,
        Diagnostic: status is { } s ? Http(s) : null);

    public static AiError NotPermitted(string provider, int status, string? errorType) => new(
        AiErrorCodes.InvalidKey,
        provider,
        $"{provider} does not allow this API key to make this request. {CloudSafe} Check the account the key belongs to.",
        HttpStatus: status,
        Diagnostic: Http(status, errorType));

    public static AiError RateLimited(string provider, TimeSpan? retryAfter, int? status = 429, string? errorType = null) => new(
        AiErrorCodes.RateLimited,
        provider,
        retryAfter is { } wait
            ? $"{provider} is limiting requests right now. {CloudSafe} Try again in {Duration(wait)}."
            : $"{provider} is limiting requests right now. {CloudSafe} Try again in a minute.",
        RetryAfter: retryAfter,
        HttpStatus: status,
        Diagnostic: status is { } s ? Http(s, errorType) : errorType);

    public static AiError QuotaExhausted(string provider, int status, string? errorType) => new(
        AiErrorCodes.ProviderError,
        provider,
        $"{provider} reports that the account has no credit or quota left. {CloudSafe} Check billing for the API key's account.",
        HttpStatus: status,
        Diagnostic: Http(status, errorType));

    /// <summary>The exact copy from the brief: "Claude could not be reached: no network. Nothing was sent twice and no document was changed."</summary>
    public static AiError NoNetwork(string provider, string? diagnostic = null) => new(
        AiErrorCodes.Network,
        provider,
        $"{provider} could not be reached: no network. {CloudSafe}",
        Diagnostic: diagnostic);

    public static AiError ConnectionFailed(string provider, string? diagnostic = null) => new(
        AiErrorCodes.Network,
        provider,
        $"{provider} could not be reached: the connection failed. {CloudSafe} Check the connection and try again.",
        Diagnostic: diagnostic);

    public static AiError TimedOut(string provider, TimeSpan after) => new(
        AiErrorCodes.Network,
        provider,
        $"{provider} did not answer within {Duration(after)}. {CloudSafe} Try again; long inputs take longer.",
        Diagnostic: "timeout");

    public static AiError ServerFailed(string provider, int status, string? errorType = null) => new(
        AiErrorCodes.ProviderError,
        provider,
        $"{provider} had a problem on its side ({Http(status)}). {CloudSafe} Try again in a few minutes.",
        HttpStatus: status,
        Diagnostic: Http(status, errorType));

    public static AiError Rejected(string provider, int status, string? errorType = null) => new(
        AiErrorCodes.ProviderError,
        provider,
        $"{provider} rejected the request ({Http(status)}). {CloudSafe} This is a problem in Memento; please report it.",
        HttpStatus: status,
        Diagnostic: Http(status, errorType));

    /// <summary>The answer was a redirect: Memento does not follow one, so the key and the text never go to another address.</summary>
    public static AiError Redirected(string provider, int status, string? toHost) => new(
        AiErrorCodes.ProviderError,
        provider,
        toHost is { Length: > 0 }
            ? $"{provider} answered with a redirect to {toHost} ({Http(status)}), and Memento does not follow redirects, so nothing was sent there. {CloudSafe} If a proxy or network filter is in between, allow the provider's address, then try again."
            : $"{provider} answered with a redirect ({Http(status)}), and Memento does not follow redirects, so nothing was sent elsewhere. {CloudSafe} If a proxy or network filter is in between, allow the provider's address, then try again.",
        HttpStatus: status,
        Diagnostic: toHost is { Length: > 0 } ? Http(status) + " to " + toHost : Http(status));

    public static AiError Unreadable(string provider, string diagnostic) => new(
        AiErrorCodes.ProviderError,
        provider,
        $"{provider} sent an answer Memento could not read. {CloudSafe} Try again.",
        Diagnostic: diagnostic);

    public static AiError StreamFailed(string provider, string? errorType) => new(
        AiErrorCodes.ProviderError,
        provider,
        $"{provider} stopped in the middle of its answer. {CloudSafe} Try again.",
        Diagnostic: errorType);

    public static AiError ContentTooLong(string provider, int? tokens = null, int? limit = null, int? status = null) => new(
        AiErrorCodes.ContentTooLong,
        provider,
        tokens is { } t && limit is { } l
            ? $"The text is too long for {provider}: {Count(t)} tokens, the limit is {Count(l)}. {CloudSafe} Untick some inputs or use shorter chunks."
            : $"The text is too long for {provider}. {CloudSafe} Untick some inputs or use shorter chunks.",
        HttpStatus: status,
        Diagnostic: status is { } s ? Http(s) : null);

    /// <summary>
    /// The service does not know the model chosen in Settings (or the key's account may not use it). Memento never tries
    /// another model in its place: the person chooses.
    /// </summary>
    public static AiError UnknownModel(string provider, string model, int status, string? errorType = null) => new(
        AiErrorCodes.UnknownModel,
        provider,
        $"{provider} does not offer a model called '{model}'. Nothing was generated and no document was changed. Choose one from the list in Settings › AI and privacy, or check the id.",
        HttpStatus: status,
        Diagnostic: Http(status, errorType));

    public static AiError LocalContentTooLong(string provider, int tokens, int limit) => new(
        AiErrorCodes.ContentTooLong,
        provider,
        $"The text is too long for {provider}: {Count(tokens)} tokens with the answer, the limit is {Count(limit)}. {LocalSafe} Use shorter chunks or a model with a larger context.");

    public static AiError Cancelled(string provider) => new(
        AiErrorCodes.Cancelled,
        provider,
        $"Generation with {provider} was cancelled. No document was changed.");

    public static AiError ModelNotInstalled(string provider, string model) => new(
        AiErrorCodes.ModelNotInstalled,
        provider,
        $"The local model {model} is not installed. {LocalSafe} Download it in Settings › AI and privacy.");

    public static AiError NotEnoughVram(string provider, string model, long freeBytes, long neededBytes) => new(
        AiErrorCodes.NotEnoughVram,
        provider,
        $"The graphics card has {Gigabytes(freeBytes)} of free memory and {model} needs {Gigabytes(neededBytes)}. {LocalSafe} Close apps that use the graphics card, or run the model on the processor.",
        Diagnostic: string.Create(CultureInfo.InvariantCulture, $"free={freeBytes} needed={neededBytes}"));

    /// <summary>A larger model does not fit in RAM and video memory with the reserves kept (ARCHITECTURE.md §8, "Larger models").</summary>
    public static AiError NotEnoughMemory(string provider, string model, long neededBytes, long freeBytes) => new(
        AiErrorCodes.NotEnoughMemory,
        provider,
        $"{model} needs {WholeGigabytesUp(neededBytes)} of memory; this PC has {WholeGigabytesDown(freeBytes)} free. {LocalSafe} Close other programs or choose a smaller model.",
        Diagnostic: string.Create(CultureInfo.InvariantCulture, $"needed={neededBytes} free={freeBytes}"));

    /// <summary>A recording started while a larger model was writing and capture would have been short of RAM, so the model was unloaded.</summary>
    public static AiError UnloadedForRecording(string provider, string model, long availableBytes) => new(
        AiErrorCodes.NotEnoughMemory,
        provider,
        $"{model} was unloaded because a recording started and this PC had {Gigabytes(availableBytes)} of memory free, too little to keep both. The recording is not affected. {LocalSafe} Generate the document again when the recording has finished, or choose a smaller model.",
        Diagnostic: string.Create(CultureInfo.InvariantCulture, $"recording available={availableBytes}"));

    /// <summary>An allocation on the graphics card failed (null context handle, failed weight upload, no KV slot).</summary>
    public static AiError GpuOutOfMemory(string provider, string model, long? freeBytes, string diagnostic) => new(
        AiErrorCodes.NotEnoughVram,
        provider,
        freeBytes is { } free and > 0
            ? $"The graphics card ran out of memory while loading {model} ({Gigabytes(free)} was free). {LocalSafe} Close apps that use the graphics card, or run the model on the processor."
            : $"The graphics card ran out of memory while loading {model}. {LocalSafe} Close apps that use the graphics card, or run the model on the processor.",
        Diagnostic: diagnostic);

    /// <summary>The graphics card cannot be used at all (no Vulkan driver).</summary>
    public static AiError GpuUnavailable(string provider, string model) => new(
        AiErrorCodes.NotEnoughVram,
        provider,
        $"The graphics card could not be used for {model} (no Vulkan driver was found). {LocalSafe} Update the graphics driver, or run the model on the processor.",
        Diagnostic: "Vulkan backend unavailable");

    public static AiError VramSpilled(string provider, string model, long sharedBytes) => new(
        AiErrorCodes.NotEnoughVram,
        provider,
        $"The graphics card ran out of its own memory while {model} was running ({Gigabytes(sharedBytes)} moved to shared memory, which is many times slower), so it was stopped. {LocalSafe} Close apps that use the graphics card, or run the model on the processor.",
        Diagnostic: string.Create(CultureInfo.InvariantCulture, $"sharedGrowth={sharedBytes}"));

    public static AiError WorkerCrashed(string provider, string model, string? diagnostic = null) => new(
        AiErrorCodes.WorkerCrashed,
        provider,
        $"The local model {model} stopped unexpectedly. {LocalSafe} Try again, or run the model on the processor.",
        Diagnostic: diagnostic);

    public static AiError LocalFailed(string provider, string model, string diagnostic) => new(
        AiErrorCodes.ProviderError,
        provider,
        $"The local model {model} could not finish the answer. {LocalSafe} Try again, or run the model on the processor.",
        Diagnostic: diagnostic);

    // "Your own server" (2.5.0). Each sentence names the address (and the model where it matters), what is safe and the fix.

    /// <summary>
    /// Nothing answered at the address. A refused connection starts with the brief's sentence ("Nothing is listening at
    /// http://localhost:11434."); an unreachable host or a name that does not resolve says it could not be reached.
    /// </summary>
    public static AiError ServerUnreachable(string provider, string address, bool refused, string? diagnostic = null) => new(
        AiErrorCodes.ServerUnreachable,
        provider,
        refused
            ? $"{ServerUnreachableSentence(address, refused)} Nothing was sent and no document was changed. Start Ollama or LM Studio, or check the address in Settings › AI and privacy."
            : $"{ServerUnreachableSentence(address, refused)} Nothing was sent and no document was changed. Check that the computer is on and the server is running, or check the address in Settings › AI and privacy.",
        Diagnostic: diagnostic);

    /// <summary>The first sentence of <see cref="ServerUnreachable"/>; "Test connection" shows it with its fix only.</summary>
    public static string ServerUnreachableSentence(string address, bool refused) => refused
        ? $"Nothing is listening at {address}."
        : $"The server at {address} could not be reached.";

    public static AiError ServerTimedOut(string provider, string address, TimeSpan after) => new(
        AiErrorCodes.Network,
        provider,
        $"The server at {address} did not answer within {Duration(after)}. Nothing was sent twice and no document was changed. A large model can take a while to load; try again, or check that the server is running.",
        Diagnostic: "timeout");

    /// <summary>Something answered, but not like an OpenAI-compatible server (a 404 or 405 on the API path, a page instead of JSON).</summary>
    public static AiError ServerNotCompatible(string provider, string address, int? status, string path) => new(
        AiErrorCodes.ServerNotCompatible,
        provider,
        status is { } s
            ? $"The server at {address} does not answer like an OpenAI-compatible server ({Http(s)} at {path}). Nothing was generated and no document was changed. Check the address in Settings › AI and privacy: Memento adds {path} to it."
            : $"The server at {address} does not answer like an OpenAI-compatible server (no answer Memento could read at {path}). Nothing was generated and no document was changed. Check the address in Settings › AI and privacy: Memento adds {path} to it.",
        HttpStatus: status,
        Diagnostic: status is { } code ? Http(code) : "not compatible");

    public static AiError ServerNeedsKey(string provider, string address, int status, bool hasKey) => new(
        AiErrorCodes.ServerNeedsKey,
        provider,
        hasKey
            ? $"The server at {address} did not accept the saved key ({Http(status)}). Nothing was generated and no document was changed. Replace the server's key in Settings › AI and privacy."
            : $"The server at {address} asks for a key ({Http(status)}). Nothing was generated and no document was changed. Add the server's key in Settings › AI and privacy.",
        HttpStatus: status,
        Diagnostic: Http(status));

    /// <summary>The same shape as <see cref="UnknownModel"/>: the server does not offer the model, and no other is tried.</summary>
    public static AiError ServerModelUnknown(string provider, string address, string model, int? status) => new(
        AiErrorCodes.ServerModelUnknown,
        provider,
        $"The server at {address} does not offer a model called '{model}'. Nothing was generated and no document was changed. Choose one of its models in Settings › AI and privacy (Test connection lists them), or check the name.",
        HttpStatus: status,
        Diagnostic: status is { } s ? Http(s) : "model not found");

    /// <summary>The server refused the request for a reason Memento does not recognise (a 400 that is not about the model, the schema or the context).</summary>
    public static AiError ServerRejected(string provider, string address, string model, int status, string? errorType = null) => new(
        AiErrorCodes.ProviderError,
        provider,
        $"The server at {address} refused the request ({Http(status, errorType)}). Nothing was generated and no document was changed. Check that {model} runs there and that the server is OpenAI-compatible; if both are right, please report it.",
        HttpStatus: status,
        Diagnostic: Http(status, errorType));

    public static AiError ServerError(string provider, string address, int status, string? errorType = null) => new(
        AiErrorCodes.ProviderError,
        provider,
        $"The server at {address} had a problem on its side ({Http(status, errorType)}). Nothing was sent twice and no document was changed. Check the server's own window or log (a model too large for its memory is a common cause), then try again.",
        HttpStatus: status,
        Diagnostic: Http(status, errorType));

    public static AiError ServerInsecure(string provider, string address) => new(
        AiErrorCodes.ServerInsecure,
        provider,
        $"{address} is outside this PC and your local network, and Memento sends there only over https. Nothing was sent and no document was changed. Use an https:// address in Settings › AI and privacy.");

    /// <summary>The server said the prompt does not fit its context (llama.cpp, vLLM, LM Studio and Ollama word it differently).</summary>
    public static AiError ServerContentTooLong(string provider, string address, string model, int? status, int? tokens = null, int? limit = null) => new(
        AiErrorCodes.ContentTooLong,
        provider,
        tokens is { } t && limit is { } l
            ? $"The text is too long for {model} at {address}: {Count(t)} tokens, the server's limit is {Count(l)}. Nothing was sent twice and no document was changed. Set the context in Settings › AI and privacy to what the server runs, or raise it on the server."
            : $"The text is too long for {model} at {address}. Nothing was sent twice and no document was changed. Set the context in Settings › AI and privacy to what the server runs, or raise it on the server.",
        HttpStatus: status,
        Diagnostic: status is { } s ? Http(s) : "context");

    internal static string Duration(TimeSpan span)
    {
        var seconds = (int)Math.Ceiling(Math.Max(1, span.TotalSeconds));
        if (seconds == 1)
        {
            return "1 second";
        }

        if (seconds < 120)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds} seconds");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Ceiling(seconds / 60.0)} minutes");
    }

    internal static string WholeGigabytesUp(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(bytes / (1024.0 * 1024 * 1024)):0} GB");

    internal static string WholeGigabytesDown(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(Math.Max(0, bytes) / (1024.0 * 1024 * 1024)):0} GB");

    internal static string Gigabytes(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.0} GB");

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Http(int status, string? errorType = null) =>
        errorType is null
            ? string.Create(CultureInfo.InvariantCulture, $"HTTP {status}")
            : string.Create(CultureInfo.InvariantCulture, $"HTTP {status} {errorType}");
}
