using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge;

/// <summary>Source-generated serialization for every bridge contract. Unknown request fields are rejected.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BridgeRequest))]
[JsonSerializable(typeof(BridgeResponse))]
[JsonSerializable(typeof(BridgeError))]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(AppVersionResult))]
[JsonSerializable(typeof(SettingsSnapshot))]
[JsonSerializable(typeof(SettingsSetParams))]
[JsonSerializable(typeof(LibraryListResult))]
[JsonSerializable(typeof(OpenExternalParams))]
[JsonSerializable(typeof(OpenExternalResult))]
[JsonSerializable(typeof(BridgeEventEnvelope<ThemeChangedPayload>))]
[JsonSerializable(typeof(BridgeEventEnvelope<FooterStatusPayload>))]
public sealed partial class BridgeJsonContext : JsonSerializerContext
{
}
