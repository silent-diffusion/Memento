using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.History;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.getVersion</c>: a kept version of the transcript, to read (Review opens it from History).</summary>
public sealed class TranscriptGetVersionMethod(HistoryService history) : BridgeMethod<TranscriptRestoreVersionParams, TranscriptResult>
{
    public override string Name => BridgeMethodNames.TranscriptGetVersion;

    public override JsonTypeInfo<TranscriptRestoreVersionParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptRestoreVersionParams;

    public override JsonTypeInfo<TranscriptResult> ResultTypeInfo => BridgeJsonContext.Default.TranscriptResult;

    public override async Task<TranscriptResult> InvokeAsync(TranscriptRestoreVersionParams parameters, CancellationToken cancellationToken) =>
        new(await history.GetTranscriptVersionAsync(parameters.RecordingId, parameters.VersionId, cancellationToken));
}
