using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>export.cancel</c>: the files written so far are removed.</summary>
public sealed class ExportCancelMethod(ExportService exports) : BridgeMethod<JobIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ExportCancel;

    public override JsonTypeInfo<JobIdParams> ParamsTypeInfo => M3BridgeJsonContext.Default.JobIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M3BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(JobIdParams parameters, CancellationToken cancellationToken)
    {
        exports.Cancel(parameters.JobId);
        return Task.FromResult(new EmptyResult());
    }
}
