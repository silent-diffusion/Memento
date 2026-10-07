using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>engine.status</c>: the resource probe's view of the transcription and speaker engines now.</summary>
public sealed class EngineStatusMethod(EngineStatusService engines) : BridgeMethod<EmptyParams, EngineStatusResult>
{
    public override string Name => BridgeMethodNames.EngineStatus;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<EngineStatusResult> ResultTypeInfo => BridgeJsonContext.Default.EngineStatusResult;

    public override Task<EngineStatusResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(engines.Compute());
}
