namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › AI and privacy (M3). External AI is off by default; M4 uses these settings and the keys.</summary>
public sealed record AiSettingsSnapshot(bool Enabled, bool AskBeforeSend, bool KeepRecord, AiShareSnapshot Share, AiProvidersSnapshot Providers);
