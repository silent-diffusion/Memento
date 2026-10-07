using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>export.estimate</c>: the Export dialog's "n files · about {size}".</summary>
public sealed class ExportEstimateMethod(ExportService exports) : BridgeMethod<ExportEstimateParams, ExportEstimate>
{
    public override string Name => BridgeMethodNames.ExportEstimate;

    public override JsonTypeInfo<ExportEstimateParams> ParamsTypeInfo => M3BridgeJsonContext.Default.ExportEstimateParams;

    public override JsonTypeInfo<ExportEstimate> ResultTypeInfo => M3BridgeJsonContext.Default.ExportEstimate;

    public override Task<ExportEstimate> InvokeAsync(ExportEstimateParams parameters, CancellationToken cancellationToken) =>
        exports.EstimateAsync(parameters.RecordingId, parameters.Selection, cancellationToken);
}
