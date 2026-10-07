using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.restoreVersion</c>: the replaced transcript becomes a version itself.</summary>
public sealed class TranscriptRestoreVersionMethod(TranscriptService transcripts) : BridgeMethod<TranscriptRestoreVersionParams, TranscriptResult>
{
    public override string Name => BridgeMethodNames.TranscriptRestoreVersion;

    public override JsonTypeInfo<TranscriptRestoreVersionParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptRestoreVersionParams;

    public override JsonTypeInfo<TranscriptResult> ResultTypeInfo => BridgeJsonContext.Default.TranscriptResult;

    public override async Task<TranscriptResult> InvokeAsync(TranscriptRestoreVersionParams parameters, CancellationToken cancellationToken) =>
        new(await transcripts.RestoreVersionAsync(parameters.RecordingId, parameters.VersionId, cancellationToken));
}
