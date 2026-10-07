namespace Memento.Generation.Ai;

/// <summary>
/// End-to-end tests point a cloud provider at a fake server on this PC through an environment variable
/// (<see cref="AnthropicVariable"/>, <see cref="OpenAiVariable"/>). Only a loopback address is honoured, so the variable can
/// never send a key or a transcript anywhere but this PC; anything else is ignored and the real endpoint is used.
/// </summary>
public static class TestEndpoints
{
    public const string AnthropicVariable = "MEMENTO_TEST_ANTHROPIC_URL";
    public const string OpenAiVariable = "MEMENTO_TEST_OPENAI_URL";

    /// <summary>The loopback URL in <paramref name="variable"/> (ending with a slash), or <c>null</c>.</summary>
    public static Uri? Loopback(string variable) => Parse(Environment.GetEnvironmentVariable(variable));

    public static Uri? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !uri.IsLoopback)
        {
            return null;
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
