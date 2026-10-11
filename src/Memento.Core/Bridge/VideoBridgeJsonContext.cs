using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge;

/// <summary>
/// Source-generated serialization for the 2.0 video contracts (BRIDGE.md "Video (2.0)"), with the same options as
/// <see cref="BridgeJsonContext"/>: camelCase, unknown request fields rejected.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(RecordingIdParams))]
[JsonSerializable(typeof(VideoSourcesResult))]
[JsonSerializable(typeof(VideoThumbnailParams))]
[JsonSerializable(typeof(VideoThumbnailResult))]
[JsonSerializable(typeof(RecordingSetVideoSourceParams))]
[JsonSerializable(typeof(VideoTracksResult))]
[JsonSerializable(typeof(RecordingSetPipParams))]
[JsonSerializable(typeof(VideoOpenPrivacySettingsParams))]
[JsonSerializable(typeof(VideoRemoveResult))]
[JsonSerializable(typeof(BridgeEventEnvelope<RecordingVideoStatusPayload>))]
[JsonSerializable(typeof(BridgeEventEnvelope<RecordingVideoProblemPayload>))]
[JsonSerializable(typeof(BridgeEventEnvelope<EmptyPayload>))]
[JsonSerializable(typeof(VideoSettingsSnapshot))]
[JsonSerializable(typeof(VideoSettingsPatch))]
[JsonSerializable(typeof(RecordingVideoSummary))]
[JsonSerializable(typeof(ProjectVideo))]
[JsonSerializable(typeof(RecordingSetVideoQualityParams))]
[JsonSerializable(typeof(VideoRemoveEstimateParams))]
[JsonSerializable(typeof(VideoRemoveEstimate))]
[JsonSerializable(typeof(ExportVideoChoice))]
public sealed partial class VideoBridgeJsonContext : JsonSerializerContext
{
}
