using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>UI → host: <c>{ "id": 17, "method": "library.list", "params": { … } }</c>.</summary>
public sealed record BridgeRequest(long Id, string Method, JsonElement? Params);
