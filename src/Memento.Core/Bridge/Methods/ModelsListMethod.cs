using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Models;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>models.list</c>: the catalog with what is installed now (read from disk) and what fits this PC.</summary>
public sealed class ModelsListMethod(IModelManager models, EngineSelector selector) : BridgeMethod<EmptyParams, ModelsListResult>
{
    public override string Name => BridgeMethodNames.ModelsList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<ModelsListResult> ResultTypeInfo => BridgeJsonContext.Default.ModelsListResult;

    public override Task<ModelsListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        var snapshot = selector.Sample();
        var list = models.List()
            .Select(m => new ModelInfo(
                m.Entry.Id,
                m.Entry.Kind,
                m.Entry.Name,
                m.Entry.Description,
                m.Entry.SizeBytes,
                m.Entry.License,
                m.Installed,
                m.Installing is { } done ? new ModelInstalling((int)Math.Clamp(100L * done / Math.Max(1, m.Entry.SizeBytes), 0, 100), done) : null,
                selector.IsRecommended(m.Entry, snapshot),
                m.Entry.RunsOn,
                m.Entry.MinVramBytes,
                m.Entry.AccuracyNote,
                m.Entry.Role))
            .ToList();
        return Task.FromResult(new ModelsListResult(list));
    }
}
