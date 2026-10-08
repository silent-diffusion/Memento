using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.copy</c>: the transcript as text or Markdown on the Windows clipboard (after 1.2.0).</summary>
public sealed class TranscriptCopyMethod(TranscriptClipboard clipboard) : BridgeMethod<TranscriptCopyParams, TranscriptCopyResult>
{
    public override string Name => BridgeMethodNames.TranscriptCopy;

    public override JsonTypeInfo<TranscriptCopyParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptCopyParams;

    public override JsonTypeInfo<TranscriptCopyResult> ResultTypeInfo => BridgeJsonContext.Default.TranscriptCopyResult;

    public override Task<TranscriptCopyResult> InvokeAsync(TranscriptCopyParams parameters, CancellationToken cancellationToken) =>
        clipboard.CopyAsync(parameters, cancellationToken);
}
