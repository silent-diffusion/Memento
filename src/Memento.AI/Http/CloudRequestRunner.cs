using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Memento.AI.Http;

/// <summary>
/// Sends one streamed cloud request with the shared rules: a timeout to the response headers, an idle timeout while
/// streaming and a total timeout; retries only for answers that say the request was not processed (429, 500, 502,
/// 503, 504, 529, or an overload event before any text), honouring <c>retry-after</c> up to a limit; every other
/// failure mapped to an <see cref="AiError"/>. Logs carry the provider, purpose, attempt, status and error type only:
/// never the key, the prompt, the answer or the provider's error text.
/// </summary>
internal sealed partial class CloudRequestRunner(HttpClient http, CloudHttpOptions options, TimeProvider time, ITokenCounter counter, ILogger logger)
{
    private const int MaxErrorBodyBytes = 64 * 1024;

    private readonly ILogger _logger = logger;

    public async Task<CloudOutcome> RunAsync(CloudCall call, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(options.TotalTimeout);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                progress?.Report(new AiProgress(AiProgressStage.Sending, Attempt: attempt));
                LogAttempt(call.ProviderName, call.Purpose, attempt);
                var attemptStarted = time.GetTimestamp();
                using var request = call.CreateRequest();
                HttpResponseMessage response;
                using (var headers = CancellationTokenSource.CreateLinkedTokenSource(total.Token))
                {
                    headers.CancelAfter(options.ResponseTimeout);
                    try
                    {
                        response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw Fail(call, AiErrors.TimedOut(call.ProviderName, total.IsCancellationRequested ? options.TotalTimeout : options.ResponseTimeout));
                    }
                    catch (HttpRequestException ex)
                    {
                        throw Fail(call, MapNetwork(call.ProviderName, ex), ex);
                    }
                }

                using (response)
                {
                    // The shared client follows no redirects (AiHttpClient): one would carry the key header and, for a
                    // 307/308, the whole request body to wherever the Location points.
                    if ((int)response.StatusCode is >= 300 and < 400)
                    {
                        throw Fail(call, AiErrors.Redirected(call.ProviderName, (int)response.StatusCode, response.Headers.Location is { IsAbsoluteUri: true } to ? to.Host : null));
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var failure = await ReadFailureAsync(response, total.Token);
                        var error = call.MapFailure(failure);
                        var retryable = IsRetryable(failure);
                        if (!retryable || attempt >= options.MaxAttempts || failure.RetryAfter > options.MaxRetryAfter)
                        {
                            throw Fail(call, error);
                        }

                        await WaitAsync(call, progress, attempt, failure.RetryAfter ?? Backoff(attempt), failure.Status, total.Token);
                        continue;
                    }

                    var context = new CloudStreamContext(progress, attempt, time, attemptStarted, counter);
                    CloudStreamResult result;
                    try
                    {
                        await using var body = await response.Content.ReadAsStreamAsync(total.Token);
                        result = await call.ReadStream(body, context, total.Token);
                    }
                    catch (CloudStreamException ex) when (ex.Retryable && !context.ProducedOutput && attempt < options.MaxAttempts)
                    {
                        await WaitAsync(call, progress, attempt, Backoff(attempt), (int)response.StatusCode, total.Token);
                        continue;
                    }
                    catch (CloudStreamException ex)
                    {
                        throw Fail(call, call.MapStreamError(ex.ErrorType));
                    }
                    catch (TimeoutException)
                    {
                        throw Fail(call, AiErrors.TimedOut(call.ProviderName, options.StreamIdleTimeout));
                    }
                    catch (CloudAnswerTooLongException)
                    {
                        throw Fail(call, AiErrors.Unreadable(call.ProviderName, "the answer is too long"));
                    }
                    catch (JsonException ex)
                    {
                        throw Fail(call, AiErrors.Unreadable(call.ProviderName, "malformed stream event: " + ex.GetType().Name));
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException && !cancellationToken.IsCancellationRequested)
                    {
                        throw Fail(call, context.ProducedOutput
                            ? AiErrors.StreamFailed(call.ProviderName, "connection lost")
                            : AiErrors.ConnectionFailed(call.ProviderName, "connection lost"));
                    }

                    var timings = new AiTimings(time.GetElapsedTime(started), context.FirstToken, null, context.Generation, attempt);
                    LogCompleted(call.ProviderName, call.Purpose, result.StopReason, result.Usage.InputTokens, result.Usage.OutputTokens, attempt, (long)timings.Total.TotalMilliseconds);
                    progress?.Report(new AiProgress(AiProgressStage.Done, OutputTokens: result.Usage.OutputTokens, Attempt: attempt));
                    return new CloudOutcome(result, timings);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && total.IsCancellationRequested)
        {
            throw Fail(call, AiErrors.TimedOut(call.ProviderName, options.TotalTimeout));
        }
    }

    internal static bool IsRetryable(CloudHttpFailure failure) => failure.Status switch
    {
        429 => failure.ErrorCode is not "insufficient_quota" && failure.ErrorType is not "insufficient_quota",
        500 or 502 or 503 or 504 or 529 => true,
        _ => false,
    };

    internal static AiError MapNetwork(string provider, HttpRequestException exception)
    {
        var socket = exception.InnerException as SocketException ?? exception.InnerException?.InnerException as SocketException;
        var noNetwork = !NetworkInterface.GetIsNetworkAvailable()
            || exception.HttpRequestError == HttpRequestError.NameResolutionError
            || socket?.SocketErrorCode is SocketError.NetworkDown or SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.HostNotFound or SocketError.TryAgain;
        var diagnostic = socket is null ? exception.HttpRequestError.ToString() : socket.SocketErrorCode.ToString();
        return noNetwork ? AiErrors.NoNetwork(provider, diagnostic) : AiErrors.ConnectionFailed(provider, diagnostic);
    }

    private TimeSpan Backoff(int attempt)
    {
        var delay = options.BaseRetryDelay * Math.Pow(2, attempt - 1);
        return delay > options.MaxRetryDelay ? options.MaxRetryDelay : delay;
    }

    private async Task WaitAsync(CloudCall call, IProgress<AiProgress>? progress, int attempt, TimeSpan delay, int status, CancellationToken cancellationToken)
    {
        LogRetry(call.ProviderName, call.Purpose, status, (long)delay.TotalMilliseconds, attempt + 1);
        progress?.Report(new AiProgress(AiProgressStage.WaitingToRetry, RetryIn: delay, Attempt: attempt + 1));
        await Task.Delay(delay, time, cancellationToken);
    }

    private static async Task<CloudHttpFailure> ReadFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var retryAfter = RetryAfter.Read(response.Headers, DateTimeOffset.UtcNow);
        string? type = null, code = null, message = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxErrorBodyBytes];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
            {
                length += read;
            }

            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                type = Text(error, "type");
                code = Text(error, "code");
                message = Text(error, "message");
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or HttpRequestException or DecoderFallbackException)
        {
            // An error without a readable body (a proxy page, a cut connection): the status is enough.
        }

        return new CloudHttpFailure(status, type, code, message, retryAfter);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private AiException Fail(CloudCall call, AiError error, Exception? inner = null)
    {
        LogFailed(call.ProviderName, call.Purpose, error.Code, SecretRedactor.Redact(error.Diagnostic) ?? "-");

        // Only socket-level exceptions are attached; their text names a host or a socket error, never a key or content.
        return new AiException(error with { Diagnostic = SecretRedactor.Redact(error.Diagnostic) }, inner);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Provider} request for {Purpose}: attempt {Attempt}")]
    private partial void LogAttempt(string provider, string purpose, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} request for {Purpose} got HTTP {Status}; attempt {Attempt} in {DelayMs} ms")]
    private partial void LogRetry(string provider, string purpose, int status, long delayMs, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Provider} request for {Purpose} finished ({StopReason}): {InputTokens} input and {OutputTokens} output tokens, {Attempts} attempt(s), {ElapsedMs} ms")]
    private partial void LogCompleted(string provider, string purpose, AiStopReason stopReason, int inputTokens, int outputTokens, int attempts, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} request for {Purpose} failed: {Code} ({Diagnostic})")]
    private partial void LogFailed(string provider, string purpose, string code, string diagnostic);
}
