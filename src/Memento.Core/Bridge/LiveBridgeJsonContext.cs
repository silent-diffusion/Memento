using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge;

/// <summary>
/// Source-generated serialization for the 2.0 contracts of the live transcript, the tray and keep-only-the-mix
/// (BRIDGE.md "Live transcript, tray and keep only the mix (2.0)"), with the same options as
/// <see cref="BridgeJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BridgeEventEnvelope<AppOpenScreenPayload>))]
[JsonSerializable(typeof(BridgeEventEnvelope<LiveTranscriptPayload>))]
public sealed partial class LiveBridgeJsonContext : JsonSerializerContext
{
}
