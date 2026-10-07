using System.Text.Json.Serialization.Metadata;
using Memento.Core.Attachments;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>attachments.open</c>: the Windows default app (programs and scripts open their folder instead).</summary>
public sealed class AttachmentsOpenMethod(AttachmentService attachments) : BridgeMethod<AttachmentIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.AttachmentsOpen;

    public override JsonTypeInfo<AttachmentIdParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AttachmentIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M3BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(AttachmentIdParams parameters, CancellationToken cancellationToken)
    {
        await attachments.OpenAsync(parameters.RecordingId, parameters.AttachmentId, cancellationToken);
        return new EmptyResult();
    }
}
