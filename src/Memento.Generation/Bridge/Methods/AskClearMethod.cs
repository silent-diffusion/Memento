using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Ask;

namespace Memento.Generation.Bridge.Methods;
/// <summary><c>ask.clear</c> (2.9.0): deletes the recording's Ask conversation (after the page's confirmation).</summary>
public sealed class AskClearMethod(AskService ask) : M4Method<RecordingIdParams, AskConversationResult>
{
    public override string Name => BridgeMethodNames.AskClear;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<AskConversationResult> ResultTypeInfo => M4BridgeJsonContext.Default.AskConversationResult;

    public override Task<AskConversationResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        ask.ClearAsync(parameters.RecordingId, cancellationToken);
}