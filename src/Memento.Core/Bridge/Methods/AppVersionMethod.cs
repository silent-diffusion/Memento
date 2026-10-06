using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>app.version</c> → <see cref="AppVersionResult"/>.</summary>
public sealed class AppVersionMethod(IAppInfo appInfo, IThemeState theme) : BridgeMethod<EmptyParams, AppVersionResult>
{
    public override string Name => BridgeMethodNames.AppVersion;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<AppVersionResult> ResultTypeInfo => BridgeJsonContext.Default.AppVersionResult;

    public override Task<AppVersionResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(new AppVersionResult(appInfo.Version, appInfo.OsVersion, theme.IsDark));
}
