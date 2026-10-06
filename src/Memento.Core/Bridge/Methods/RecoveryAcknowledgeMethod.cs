using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Recovery;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recovery.acknowledge</c>: dismisses the recovery dialog for one recording. The recording is untouched.</summary>
public sealed class RecoveryAcknowledgeMethod(RecoveryService recovery) : BridgeMethod<RecordingIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.RecoveryAcknowledge;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken)
    {
        try
        {
            await recovery.AcknowledgeAsync(parameters.RecordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(parameters.RecordingId);
        }

        return new EmptyResult();
    }
}
