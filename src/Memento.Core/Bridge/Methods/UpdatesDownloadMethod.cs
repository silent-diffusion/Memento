using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Updates;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>updates.download</c> → <see cref="UpdateStatus"/> ("Update now", 2.1.0): downloads the version "Check now" found,
/// in the background once no recording or processing runs; <c>updates.progress</c> follows it to <c>ready</c>.
/// <c>updates.nothingToDownload</c> when nothing is <c>available</c>; <c>updates.unavailable</c> for a copy that cannot
/// update itself.
/// </summary>
public sealed class UpdatesDownloadMethod(UpdateService updates) : BridgeMethod<EmptyParams, UpdateStatus>
{
    public override string Name => BridgeMethodNames.UpdatesDownload;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => UpdatesBridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<UpdateStatus> ResultTypeInfo => UpdatesBridgeJsonContext.Default.UpdateStatus;

    public override Task<UpdateStatus> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        updates.DownloadNowAsync(cancellationToken);
}
