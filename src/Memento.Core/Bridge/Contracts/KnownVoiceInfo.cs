namespace Memento.Core.Bridge.Contracts;

/// <summary>A voice in Settings › Known voices (2.0). Its signature never crosses the bridge.</summary>
/// <param name="Recordings">How many recordings the name was confirmed in.</param>
/// <param name="Suggest">"Suggest this voice": off keeps the voice but suggests it nowhere.</param>
public sealed record KnownVoiceInfo(string Id, string Name, int Recordings, DateTimeOffset LastConfirmedAt, bool Suggest);
