using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>export.run</c>: checks the destination, then writes in the background (<c>export.progress</c>).</summary>
public sealed class ExportRunMethod(ExportService exports) : BridgeMethod<ExportRunParams, JobIdResult>
{
    public override string Name => BridgeMethodNames.ExportRun;

    public override JsonTypeInfo<ExportRunParams> ParamsTypeInfo => M3BridgeJsonContext.Default.ExportRunParams;

    public override JsonTypeInfo<JobIdResult> ResultTypeInfo => M3BridgeJsonContext.Default.JobIdResult;

    public override async Task<JobIdResult> InvokeAsync(ExportRunParams parameters, CancellationToken cancellationToken) =>
        new(await exports.RunAsync(parameters, cancellationToken));
}
