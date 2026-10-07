using System.Text.Json.Serialization.Metadata;
using Memento.Core.Attachments;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>attachments.remove</c>: the UI confirms first.</summary>
public sealed class AttachmentsRemoveMethod(AttachmentService attachments) : BridgeMethod<AttachmentIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.AttachmentsRemove;

    public override JsonTypeInfo<AttachmentIdParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AttachmentIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M3BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(AttachmentIdParams parameters, CancellationToken cancellationToken)
    {
        await attachments.RemoveAsync(parameters.RecordingId, parameters.AttachmentId, cancellationToken);
        return new EmptyResult();
    }
}
