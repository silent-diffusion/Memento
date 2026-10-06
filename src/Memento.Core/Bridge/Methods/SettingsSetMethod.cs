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
    public const string InvalidValueCode = DomainErrorCodes.SettingsInvalidValue;
    public const string LibraryMoveUnavailableCode = DomainErrorCodes.SettingsLibraryMoveUnavailable;

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

        var recording = parameters.Recording is null ? null : Replace(store.Current.Recording, parameters.Recording);
        if (recording?.Validate() is { } problem)
        {
            throw new BridgeException(InvalidValueCode, problem + " Nothing was changed.");
        }

        var updated = await store.UpdateAsync(
            current => current with
            {
                Theme = parameters.Theme ?? current.Theme,
                ListDensity = parameters.ListDensity ?? current.ListDensity,
                Recording = recording is null ? current.Recording : Replace(current.Recording, parameters.Recording!),
            },
            cancellationToken);

        return SettingsGetMethod.ToSnapshot(updated);
    }

    /// <summary>
    /// The <c>recording</c> block is replaced whole when present (BRIDGE.md: the UI always sends the full block):
    /// a field it leaves out takes its default, not its previous value. A lossy codec without a bitrate uses
    /// <see cref="StorageSettings.DefaultLossyBitrateKbps"/>; FLAC never carries one. Unknown fields kept in
    /// <paramref name="current"/>'s extension data survive.
    /// </summary>
    internal static RecordingSettings Replace(RecordingSettings current, RecordingSettingsPatch patch)
    {
        var defaults = new RecordingSettings { ExtensionData = current.ExtensionData };
        var storage = defaults.Storage with { ExtensionData = current.Storage.ExtensionData };
        if (patch.Storage is { } storagePatch)
        {
            var codec = storagePatch.Codec ?? storage.Codec;
            var lossy = codec != StorageSettings.Flac;
            int? bitrate = lossy ? storagePatch.BitrateKbps ?? storage.BitrateKbps ?? StorageSettings.DefaultLossyBitrateKbps : null;
            storage = storage with
            {
                Codec = codec,
                BitrateKbps = bitrate,
                DownmixMono = storagePatch.DownmixMono ?? storage.DownmixMono,
                KeepOnlyMix = storagePatch.KeepOnlyMix ?? storage.KeepOnlyMix,
            };
        }

        return defaults with
        {
            DefaultType = patch.DefaultType?.Trim() ?? defaults.DefaultType,
            DefaultSourceIds = patch.DefaultSourceIds?.ToList() ?? defaults.DefaultSourceIds,
            KeepSeparateTracks = patch.KeepSeparateTracks ?? defaults.KeepSeparateTracks,
            Storage = storage,
            CheckpointSeconds = patch.CheckpointSeconds ?? defaults.CheckpointSeconds,
            LowSpaceGb = patch.LowSpaceGb ?? defaults.LowSpaceGb,
        };
    }
}
