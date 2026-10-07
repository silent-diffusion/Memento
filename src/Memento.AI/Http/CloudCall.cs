namespace Memento.AI.Http;

/// <summary>One cloud request for <see cref="CloudRequestRunner"/>: how to build it, read it and name its failures.</summary>
/// <param name="CreateRequest">A fresh message per attempt (headers carry the key; the body is the same bytes each time).</param>
/// <param name="ReadStream">Parses the event stream, appending text to the context; throws <see cref="CloudStreamException"/> for error events.</param>
/// <param name="MapFailure">The error for a non-success HTTP answer.</param>
/// <param name="MapStreamError">The error for an error event in the stream (argument: the provider's error type).</param>
internal sealed record CloudCall(
    string ProviderName,
    string Purpose,
    Func<HttpRequestMessage> CreateRequest,
    Func<Stream, CloudStreamContext, CancellationToken, Task<CloudStreamResult>> ReadStream,
    Func<CloudHttpFailure, AiError> MapFailure,
    Func<string?, AiError> MapStreamError);
