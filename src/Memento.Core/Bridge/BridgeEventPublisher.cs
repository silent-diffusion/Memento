using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge;

/// <summary>Serializes typed host events into <see cref="BridgeEventEnvelope{TPayload}"/> JSON and posts them to the UI.</summary>
public sealed class BridgeEventPublisher(IBridgeEventSink sink)
{
    public void PublishThemeChanged(ThemeChangedPayload payload) => sink.Post(SerializeThemeChanged(payload));

    public void PublishFooterStatus(FooterStatusPayload payload) => sink.Post(SerializeFooterStatus(payload));

    public void PublishRecordingState(RecordingStatePayload payload) =>
        sink.Post(Serialize(BridgeEventNames.RecordingState, payload, BridgeJsonContext.Default.BridgeEventEnvelopeRecordingStatePayload));

    public void PublishRecordingLevels(RecordingLevelsPayload payload) =>
        sink.Post(Serialize(BridgeEventNames.RecordingLevels, payload, BridgeJsonContext.Default.BridgeEventEnvelopeRecordingLevelsPayload));

    public void PublishSourceLost(SourceLostPayload payload) =>
        sink.Post(Serialize(BridgeEventNames.RecordingSourceLost, payload, BridgeJsonContext.Default.BridgeEventEnvelopeSourceLostPayload));

    public void PublishStoppedByHost(StoppedByHostPayload payload) =>
        sink.Post(Serialize(BridgeEventNames.RecordingStoppedByHost, payload, BridgeJsonContext.Default.BridgeEventEnvelopeStoppedByHostPayload));

    public void PublishLibraryChanged(LibraryChangedPayload payload) =>
        sink.Post(Serialize(BridgeEventNames.LibraryChanged, payload, BridgeJsonContext.Default.BridgeEventEnvelopeLibraryChangedPayload));

    public void PublishProcessingProgress(ProcessingProgressPayload payload) =>
        sink.Post(Serialize(BridgeEventNames.ProcessingProgress, payload, BridgeJsonContext.Default.BridgeEventEnvelopeProcessingProgressPayload));

    public void PublishLowSpace(StorageLowSpacePayload payload) =>
        sink.Post(Serialize(BridgeEventNames.StorageLowSpace, payload, BridgeJsonContext.Default.BridgeEventEnvelopeStorageLowSpacePayload));

    public static string SerializeThemeChanged(ThemeChangedPayload payload) =>
        Serialize(BridgeEventNames.ThemeChanged, payload, BridgeJsonContext.Default.BridgeEventEnvelopeThemeChangedPayload);

    public static string SerializeFooterStatus(FooterStatusPayload payload) =>
        Serialize(BridgeEventNames.FooterStatus, payload, BridgeJsonContext.Default.BridgeEventEnvelopeFooterStatusPayload);

    public static string Serialize<TPayload>(string eventName, TPayload payload, JsonTypeInfo<BridgeEventEnvelope<TPayload>> typeInfo) =>
        JsonSerializer.Serialize(new BridgeEventEnvelope<TPayload>(eventName, payload), typeInfo);
}
