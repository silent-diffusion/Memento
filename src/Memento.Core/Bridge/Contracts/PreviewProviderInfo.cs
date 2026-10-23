namespace Memento.Core.Bridge.Contracts;

/// <summary>2.0: who "Preview what will be sent" is for.</summary>
/// <param name="Id"><c>anthropic</c>, <c>openai</c>, <c>server</c> or <c>local</c>.</param>
/// <param name="Name">"Claude", "ChatGPT", "Your own server", "Local model".</param>
/// <param name="Kind"><c>cloud</c>, <c>server</c> (2.5.0) or <c>local</c>.</param>
/// <param name="ModelLabel">The model in effect ("claude-opus-5-5", "Qwen3.5 4B", "qwen3:8b at localhost:11434"), or <c>null</c>.</param>
public sealed record PreviewProviderInfo(string Id, string Name, string Kind, string? ModelLabel);
