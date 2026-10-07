namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › AI and privacy (M3, M4). External AI is off by default.</summary>
public sealed record AiSettingsSnapshot(bool Enabled, bool AskBeforeSend, bool KeepRecord, AiShareSnapshot Share, AiProvidersSnapshot Providers)
{
    /// <summary><c>anthropic</c>, <c>openai</c>, <c>local</c>, or <c>null</c> when no default is chosen (M4).</summary>
    public string? DefaultProviderId { get; init; }

    /// <summary>The local model in effect: the one chosen in Settings, or the one this PC's hardware suits (M4).</summary>
    public string? LocalModelId { get; init; }

    /// <summary><c>true</c> when <see cref="LocalModelId"/> was chosen in Settings rather than picked by hardware (M4).</summary>
    public bool LocalModelChosen { get; init; }
}
