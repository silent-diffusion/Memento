using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>processing.pause</c>: holds transcription and speakers until <c>processing.resume</c> (the footer says so).</summary>
public sealed class ProcessingPauseMethod(ProcessingGate gate) : BridgeMethod<EmptyParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ProcessingPause;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        gate.SetManual(true);
        return Task.FromResult(new EmptyResult());
    }
}
