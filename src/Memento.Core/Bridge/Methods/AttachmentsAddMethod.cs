using System.Text.Json.Serialization.Metadata;
using Memento.Core.Attachments;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>attachments.add</c>: the file picker (or a given path); 100 MB per file.</summary>
public sealed class AttachmentsAddMethod(AttachmentService attachments) : BridgeMethod<AttachmentsAddParams, AttachmentAddResult>
{
    public override string Name => BridgeMethodNames.AttachmentsAdd;

    public override JsonTypeInfo<AttachmentsAddParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AttachmentsAddParams;

    public override JsonTypeInfo<AttachmentAddResult> ResultTypeInfo => M3BridgeJsonContext.Default.AttachmentAddResult;

    public override Task<AttachmentAddResult> InvokeAsync(AttachmentsAddParams parameters, CancellationToken cancellationToken)
    {
        PickedFilesOnly.Require(Name, parameters.Path);
        return attachments.AddAsync(parameters.RecordingId, null, cancellationToken);
    }
}
