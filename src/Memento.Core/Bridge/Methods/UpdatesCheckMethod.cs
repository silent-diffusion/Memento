using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Updates;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>updates.check</c> → <see cref="UpdateStatus"/> once the feed answered ("Check now"). A newer version then downloads
/// in the background (once no recording or processing runs); <c>updates.progress</c> follows it. A failed check is not
/// an error: the status is <c>failed</c> with the reason in <c>message</c>.
/// </summary>
public sealed class UpdatesCheckMethod(UpdateService updates) : BridgeMethod<EmptyParams, UpdateStatus>
{
    public override string Name => BridgeMethodNames.UpdatesCheck;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => UpdatesBridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<UpdateStatus> ResultTypeInfo => UpdatesBridgeJsonContext.Default.UpdateStatus;

    public override Task<UpdateStatus> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        updates.CheckNowAsync(cancellationToken);
}
