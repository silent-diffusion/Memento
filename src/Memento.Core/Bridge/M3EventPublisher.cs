using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge;

/// <summary>Posts the M3 events: <c>export.progress</c>, <c>library.moveProgress</c>, <c>storage.reclaimProgress</c>.</summary>
public sealed class M3EventPublisher(IBridgeEventSink sink)
{
    public void PublishExportProgress(ExportProgressPayload payload) =>
        sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.ExportProgress, payload, M3BridgeJsonContext.Default.BridgeEventEnvelopeExportProgressPayload));

    public void PublishLibraryMoveProgress(LibraryMoveProgressPayload payload) =>
        sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.LibraryMoveProgress, payload, M3BridgeJsonContext.Default.BridgeEventEnvelopeLibraryMoveProgressPayload));

    public void PublishReclaimProgress(StorageReclaimProgressPayload payload) =>
        sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.StorageReclaimProgress, payload, M3BridgeJsonContext.Default.BridgeEventEnvelopeStorageReclaimProgressPayload));
}
