namespace Memento.Core.Bridge.Contracts;

/// <summary>Host → UI notification: <c>{ "event": "theme.changed", "payload": { … } }</c>.</summary>
public sealed record BridgeEventEnvelope<TPayload>(string Event, TPayload Payload);
