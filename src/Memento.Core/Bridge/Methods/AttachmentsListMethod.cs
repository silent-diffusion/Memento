using System.Text.Json.Serialization.Metadata;
using Memento.Core.Attachments;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>attachments.list</c>.</summary>
public sealed class AttachmentsListMethod(AttachmentService attachments) : BridgeMethod<RecordingIdParams, AttachmentsResult>
{
    public override string Name => BridgeMethodNames.AttachmentsList;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => M3BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<AttachmentsResult> ResultTypeInfo => M3BridgeJsonContext.Default.AttachmentsResult;

    public override async Task<AttachmentsResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        new(await attachments.ListAsync(parameters.RecordingId, cancellationToken));
}
