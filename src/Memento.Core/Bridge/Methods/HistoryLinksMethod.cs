using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.History;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>history.links</c>: the History lines that changed the transcript or a document, and the copy each opens.</summary>
public sealed class HistoryLinksMethod(HistoryService history) : BridgeMethod<RecordingIdParams, HistoryLinksResult>
{
    public override string Name => BridgeMethodNames.HistoryLinks;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<HistoryLinksResult> ResultTypeInfo => BridgeJsonContext.Default.HistoryLinksResult;

    public override async Task<HistoryLinksResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        new(await history.LinksAsync(parameters.RecordingId, cancellationToken));
}
