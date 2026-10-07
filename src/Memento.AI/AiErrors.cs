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

    internal static string Gigabytes(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.0} GB");

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Http(int status, string? errorType = null) =>
        errorType is null
            ? string.Create(CultureInfo.InvariantCulture, $"HTTP {status}")
            : string.Create(CultureInfo.InvariantCulture, $"HTTP {status} {errorType}");
}
