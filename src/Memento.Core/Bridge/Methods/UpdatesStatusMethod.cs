using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Updates;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>updates.status</c> → <see cref="UpdateStatus"/>: the running version, the last check and any download.</summary>
public sealed class UpdatesStatusMethod(UpdateService updates) : BridgeMethod<EmptyParams, UpdateStatus>
{
    public override string Name => BridgeMethodNames.UpdatesStatus;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => UpdatesBridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<UpdateStatus> ResultTypeInfo => UpdatesBridgeJsonContext.Default.UpdateStatus;

    public override Task<UpdateStatus> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(updates.Status);
}
