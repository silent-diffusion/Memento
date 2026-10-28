using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Ask;

namespace Memento.Generation.Bridge.Methods;
/// <summary><c>ask.send</c> (2.9.0): one question; with "ask before every send" a question that leaves the PC waits for <c>ask.confirm</c>.</summary>
public sealed class AskSendMethod(AskService ask) : M4Method<AskSendParams, AskSendResult>
{
    public override string Name => BridgeMethodNames.AskSend;

    public override JsonTypeInfo<AskSendParams> ParamsTypeInfo => M4BridgeJsonContext.Default.AskSendParams;

    public override JsonTypeInfo<AskSendResult> ResultTypeInfo => M4BridgeJsonContext.Default.AskSendResult;

    public override Task<AskSendResult> InvokeAsync(AskSendParams parameters, CancellationToken cancellationToken) =>
        ask.SendAsync(parameters.RecordingId, parameters.Question, parameters.ProviderId, cancellationToken);
}