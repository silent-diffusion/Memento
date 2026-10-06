using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recording.resume</c>.</summary>
public sealed class RecordingResumeMethod(RecordingCoordinator recordings) : BridgeMethod<SessionParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.RecordingResume;

    public override JsonTypeInfo<SessionParams> ParamsTypeInfo => BridgeJsonContext.Default.SessionParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(SessionParams parameters, CancellationToken cancellationToken)
    {
        await recordings.ResumeAsync(parameters.SessionId, cancellationToken);
        return new EmptyResult();
    }
}
