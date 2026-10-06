using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Host → UI answer to a <see cref="BridgeRequest"/>: <c>{ "id", "result" }</c> or <c>{ "id", "error" }</c>.
/// <see cref="Id"/> is <c>null</c> only when the request was too malformed to carry one.
/// </summary>
public sealed record BridgeResponse(
    long? Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BridgeError? Error);
