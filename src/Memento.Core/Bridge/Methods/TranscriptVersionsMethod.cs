using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.versions</c>: kept versions, newest first; empty when history is off.</summary>
public sealed class TranscriptVersionsMethod(TranscriptService transcripts) : BridgeMethod<RecordingIdParams, TranscriptVersionsResult>
{
    public override string Name => BridgeMethodNames.TranscriptVersions;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<TranscriptVersionsResult> ResultTypeInfo => BridgeJsonContext.Default.TranscriptVersionsResult;

    public override async Task<TranscriptVersionsResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        new(await transcripts.VersionsAsync(parameters.RecordingId, cancellationToken));
}
