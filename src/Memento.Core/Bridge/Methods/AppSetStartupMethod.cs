using System.Security;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>app.setStartup</c>: the current user's Windows <c>Run</c> entry, mirrored in Settings › General.</summary>
public sealed class AppSetStartupMethod(IStartupRegistration startup, ISettingsStore settings) : BridgeMethod<AppSetStartupParams, AppStartupResult>
{
    public override string Name => BridgeMethodNames.AppSetStartup;

    public override JsonTypeInfo<AppSetStartupParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AppSetStartupParams;

    public override JsonTypeInfo<AppStartupResult> ResultTypeInfo => M3BridgeJsonContext.Default.AppStartupResult;

    public override async Task<AppStartupResult> InvokeAsync(AppSetStartupParams parameters, CancellationToken cancellationToken)
    {
        Apply(startup, parameters.StartWithWindows);
        await settings.UpdateAsync(s => s with { General = s.General with { StartWithWindows = parameters.StartWithWindows } }, cancellationToken);
        return new AppStartupResult(startup.IsEnabled);
    }

    /// <summary>Changes the entry, turning a refusal into a specific error.</summary>
    internal static void Apply(IStartupRegistration startup, bool enabled)
    {
        try
        {
            startup.SetEnabled(enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or PlatformNotSupportedException)
        {
            throw new BridgeException(
                DomainErrorCodes.AppStartupRefused,
                $"Windows did not let Memento {(enabled ? "add" : "remove")} its startup entry ({ex.GetType().Name}). Nothing was changed. You can change it in Windows Settings › Apps › Startup.");
        }
    }
}
