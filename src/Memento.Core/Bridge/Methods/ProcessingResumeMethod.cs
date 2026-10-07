using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>processing.resume</c>: lifts <c>processing.pause</c> and a "PC is busy" pause in effect now; paused stages continue where they stopped.</summary>
public sealed class ProcessingResumeMethod(ProcessingGate gate) : BridgeMethod<EmptyParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ProcessingResume;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        gate.Resume();
        return Task.FromResult(new EmptyResult());
    }
}
