using Memento.Core.Host;
using Memento.Core.Secrets;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// What <c>settings.get</c> and <c>settings.set</c> need beyond the settings file from M3: which AI providers have a
/// saved key, and the Windows startup entry that Settings › General mirrors.
/// </summary>
public sealed class SettingsExtras(ISecretStore secrets, IStartupRegistration startup)
{
    public ISecretStore Secrets { get; } = secrets;

    public IStartupRegistration Startup { get; } = startup;
}
