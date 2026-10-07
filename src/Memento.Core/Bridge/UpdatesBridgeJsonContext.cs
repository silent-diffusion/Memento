using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge;

/// <summary>Source-generated serialization for <c>updates.*</c> (BRIDGE.md "Updates"), with the bridge's options.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(UpdateStatus))]
[JsonSerializable(typeof(BridgeEventEnvelope<UpdateStatus>))]
public sealed partial class UpdatesBridgeJsonContext : JsonSerializerContext
{
}
