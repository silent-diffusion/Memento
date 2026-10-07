using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>export.openFolder</c>: File Explorer at the export's folder.</summary>
public sealed class ExportOpenFolderMethod(ExportService exports) : BridgeMethod<JobIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ExportOpenFolder;

    public override JsonTypeInfo<JobIdParams> ParamsTypeInfo => M3BridgeJsonContext.Default.JobIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M3BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(JobIdParams parameters, CancellationToken cancellationToken)
    {
        exports.OpenFolder(parameters.JobId);
        return Task.FromResult(new EmptyResult());
    }
}
