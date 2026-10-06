using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>settings.set</c>: partial update. Every supplied field is validated before anything is written,
/// so a request with one bad field changes nothing.
/// </summary>
public sealed class SettingsSetMethod(ISettingsStore store) : BridgeMethod<SettingsSetParams, SettingsSnapshot>
{
    public const string InvalidValueCode = "settings.invalidValue";
    public const string LibraryMoveUnavailableCode = "settings.libraryMoveUnavailable";

    public override string Name => BridgeMethodNames.SettingsSet;

    public override JsonTypeInfo<SettingsSetParams> ParamsTypeInfo => BridgeJsonContext.Default.SettingsSetParams;

    public override JsonTypeInfo<SettingsSnapshot> ResultTypeInfo => BridgeJsonContext.Default.SettingsSnapshot;

    public override async Task<SettingsSnapshot> InvokeAsync(SettingsSetParams parameters, CancellationToken cancellationToken)
    {
        if (parameters.Theme is not null && !ThemePreference.IsValid(parameters.Theme))
        {
            throw new BridgeException(
                InvalidValueCode,
                $"Theme '{parameters.Theme}' is not available. Choose {string.Join(", ", ThemePreference.All)}.");
        }

        if (parameters.ListDensity is not null && !ListDensity.IsValid(parameters.ListDensity))
        {
            throw new BridgeException(
                InvalidValueCode,
                $"List density '{parameters.ListDensity}' is not available. Choose {string.Join(", ", ListDensity.All)}.");
        }

        if (parameters.LibraryPath is not null
            && !string.Equals(parameters.LibraryPath, store.Current.EffectiveLibraryPath, StringComparison.OrdinalIgnoreCase))
        {
            // Moving the library is copy-then-verify-then-delete with progress (ARCHITECTURE.md §4), not a settings write.
            throw new BridgeException(
                LibraryMoveUnavailableCode,
                "The library location can't be changed in this version. Your recordings stay where they are; moving the library arrives with Settings in a later version.");
        }

        var updated = await store.UpdateAsync(
            current => current with
            {
                Theme = parameters.Theme ?? current.Theme,
                ListDensity = parameters.ListDensity ?? current.ListDensity,
            },
            cancellationToken);

        return SettingsGetMethod.ToSnapshot(updated);
    }
}
