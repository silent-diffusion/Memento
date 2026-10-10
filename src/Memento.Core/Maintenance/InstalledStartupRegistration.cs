using Memento.Core.Host;

namespace Memento.Core.Maintenance;

/// <summary>
/// Start with Windows for an installed copy only (2.0): wraps the <c>Run</c> entry (<see cref="RegistryStartupRegistration"/>)
/// and refuses to add it when this copy was not installed by the Memento installer (a build folder, a copied folder).
/// Removing an entry always works, so one left by an earlier copy can still be turned off.
/// </summary>
/// <param name="isInstalled">Asked each time (Velopack's own check in the app; a fake in tests).</param>
public sealed class InstalledStartupRegistration(IStartupRegistration inner, Func<bool> isInstalled) : IStartupRegistration
{
    public const string NotInstalledReason =
        "Start with Windows works only for an installed Memento, and this copy was not installed with the Memento installer.";

    public bool IsEnabled => inner.IsEnabled;

    public bool IsAvailable => isInstalled();

    public string? UnavailableReason => IsAvailable ? null : NotInstalledReason;

    public void SetEnabled(bool enabled)
    {
        if (enabled && !IsAvailable)
        {
            throw new InvalidOperationException(NotInstalledReason);
        }

        inner.SetEnabled(enabled);
    }
}
