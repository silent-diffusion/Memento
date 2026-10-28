using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Ask;

namespace Memento.Generation.Bridge.Methods;
/// <summary><c>ask.cancel</c> (2.9.0): stops an answer mid-stream (what had arrived is kept as a stopped answer).</summary>
public sealed class AskCancelMethod(AskService ask) : M4Method<JobIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.AskCancel;

    public override JsonTypeInfo<JobIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.JobIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M4BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(JobIdParams parameters, CancellationToken cancellationToken) =>
        Done(() => ask.Cancel(parameters.JobId));
}