using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Ask;

namespace Memento.Generation.Bridge.Methods;
/// <summary><c>ask.confirm</c> (2.9.0): the answer to "ask before every send" for a question.</summary>
public sealed class AskConfirmMethod(AskService ask) : M4Method<GenerationConfirmParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.AskConfirm;

    public override JsonTypeInfo<GenerationConfirmParams> ParamsTypeInfo => M4BridgeJsonContext.Default.GenerationConfirmParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M4BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(GenerationConfirmParams parameters, CancellationToken cancellationToken) =>
        Done(ask.ConfirmAsync(parameters.JobId, parameters.Approved));
}