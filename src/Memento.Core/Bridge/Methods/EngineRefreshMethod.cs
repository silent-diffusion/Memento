using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Status;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>engine.refresh</c>: Settings' "Check again". Reads the graphics card again with nothing reused (who holds its memory
/// is otherwise cached for a few seconds), answers like <c>engine.status</c> and updates the footer if where transcription
/// runs has changed. The UI then reads <c>providers.list</c> and <c>settings.get</c> again, which use the same fresh reading.
/// </summary>
public sealed class EngineRefreshMethod(EngineStatusService engines, FooterStatusService footer) : BridgeMethod<EmptyParams, EngineStatusResult>
{
    public override string Name => BridgeMethodNames.EngineRefresh;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<EngineStatusResult> ResultTypeInfo => BridgeJsonContext.Default.EngineStatusResult;

    public override Task<EngineStatusResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        var result = engines.Refresh();
        footer.Publish(force: false);
        return Task.FromResult(result);
    }
}
