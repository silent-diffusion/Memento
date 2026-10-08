using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge;

/// <summary>Posts the M4 events: <c>generation.progress</c>, <c>generation.output</c>, <c>documents.changed</c>, <c>templates.changed</c>, <c>styles.changed</c>.</summary>
public sealed class M4EventPublisher(IBridgeEventSink sink)
{
    private static readonly EmptyPayload Empty = new();

    public void PublishGenerationProgress(GenerationProgress payload) =>
        sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.GenerationProgress, payload, M4BridgeJsonContext.Default.BridgeEventEnvelopeGenerationProgress));

    public void PublishGenerationOutput(GenerationOutput payload) =>
        sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.GenerationOutput, payload, M4BridgeJsonContext.Default.BridgeEventEnvelopeGenerationOutput));

    public void PublishDocumentsChanged(DocumentsChangedPayload payload) =>
        sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.DocumentsChanged, payload, M4BridgeJsonContext.Default.BridgeEventEnvelopeDocumentsChangedPayload));

    public void PublishTemplatesChanged() =>
        sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.TemplatesChanged, Empty, M4BridgeJsonContext.Default.BridgeEventEnvelopeEmptyPayload));

    public void PublishStylesChanged() =>
        sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.StylesChanged, Empty, M4BridgeJsonContext.Default.BridgeEventEnvelopeEmptyPayload));
}
