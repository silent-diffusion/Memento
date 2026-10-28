using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Ask;

namespace Memento.Generation.Bridge.Methods;
/// <summary><c>ask.get</c> (2.9.0): the recording's Ask conversation. Reads only; nothing is sent.</summary>
public sealed class AskGetMethod(AskService ask) : M4Method<RecordingIdParams, AskConversationResult>
{
    public override string Name => BridgeMethodNames.AskGet;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<AskConversationResult> ResultTypeInfo => M4BridgeJsonContext.Default.AskConversationResult;

    public override Task<AskConversationResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        ask.GetAsync(parameters.RecordingId, cancellationToken);
}