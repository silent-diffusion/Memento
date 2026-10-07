using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Updates;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>updates.apply</c> ("Restart to update"): Memento closes, the downloaded version installs and starts. Refused with
/// <c>updates.notReady</c> when nothing is downloaded and <c>updates.busy</c> while recording.
/// </summary>
public sealed class UpdatesApplyMethod(UpdateService updates) : BridgeMethod<EmptyParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.UpdatesApply;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => UpdatesBridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => UpdatesBridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        updates.Apply();
        return Task.FromResult(new EmptyResult());
    }
}
