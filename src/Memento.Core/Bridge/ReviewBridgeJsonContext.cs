using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge;

/// <summary>
/// Source-generated serialization for the 2.0 Review contracts (known voices, suggested chapters, selection mode), with
/// the same options as <see cref="BridgeJsonContext"/>: camelCase, unknown request fields rejected.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(RecordingIdParams))]
[JsonSerializable(typeof(KnownVoicesResult))]
[JsonSerializable(typeof(VoiceIdParams))]
[JsonSerializable(typeof(VoiceSuggestParams))]
[JsonSerializable(typeof(VoiceRememberParams))]
[JsonSerializable(typeof(VoiceRememberResult))]
[JsonSerializable(typeof(VoiceChangeParams))]
[JsonSerializable(typeof(VoiceMatchesResult))]
[JsonSerializable(typeof(VoiceDeclineParams))]
[JsonSerializable(typeof(VoiceAcceptParams))]
[JsonSerializable(typeof(VoiceAcceptResult))]
[JsonSerializable(typeof(ChapterSuggestionsResult))]
[JsonSerializable(typeof(ChapterSuggestionParams))]
[JsonSerializable(typeof(TranscriptSetSegmentsSpeakerParams))]
[JsonSerializable(typeof(SegmentsSpeakersResult))]
public sealed partial class ReviewBridgeJsonContext : JsonSerializerContext
{
}
