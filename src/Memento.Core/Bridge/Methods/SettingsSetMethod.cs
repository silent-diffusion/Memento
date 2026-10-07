using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Models;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>settings.set</c>: partial update. Every supplied field is validated before anything is written,
/// so a request with one bad field changes nothing. The <c>recording</c> block is replaced whole; the M2 blocks
/// (<c>transcription</c>, <c>speakers</c>, <c>history</c>) change only the fields they carry.
/// </summary>
public sealed class SettingsSetMethod(ISettingsStore store, EngineSelector selector, IModelManager models, SettingsExtras? extras = null) : BridgeMethod<SettingsSetParams, SettingsSnapshot>
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
                "The library location is changed with Change in Settings › Storage, which copies every recording and checks it before switching. Nothing was changed; your recordings stay where they are.");
        }

        var recording = parameters.Recording is null ? null : Replace(store.Current.Recording, parameters.Recording);
        if (recording?.Validate() is { } problem)
        {
            throw new BridgeException(InvalidValueCode, problem + " Nothing was changed.");
        }

        var transcription = parameters.Transcription is null ? null : Merge(store.Current.Transcription, parameters.Transcription);
        var speakers = parameters.Speakers is null ? null : Merge(store.Current.Speakers, parameters.Speakers);
        var history = parameters.History is null ? null : Merge(store.Current.History, parameters.History);
        if ((transcription?.Validate() ?? speakers?.Validate() ?? history?.Validate()) is { } m2Problem)
        {
            throw new BridgeException(InvalidValueCode, m2Problem + " Nothing was changed.");
        }

        ValidateModels(parameters.Transcription, parameters.Speakers);

        // M3 blocks: validated before anything is written; the startup entry changes first so a refusal changes nothing.
        if (M3SettingsBlocks.Validate(M3SettingsBlocks.Merge(store.Current, parameters)) is { } m3Problem)
        {
            throw new BridgeException(InvalidValueCode, m3Problem + " Nothing was changed.");
        }

        M3SettingsBlocks.ApplyStartup(parameters, extras);

        var updated = await store.UpdateAsync(
            current => M3SettingsBlocks.Merge(current, parameters) with
            {
                Theme = parameters.Theme ?? current.Theme,
                ListDensity = parameters.ListDensity ?? current.ListDensity,
                Recording = recording is null ? current.Recording : Replace(current.Recording, parameters.Recording!),
                Transcription = parameters.Transcription is null ? current.Transcription : Merge(current.Transcription, parameters.Transcription),
                Speakers = parameters.Speakers is null ? current.Speakers : Merge(current.Speakers, parameters.Speakers),
                History = parameters.History is null ? current.History : Merge(current.History, parameters.History),
            },
            cancellationToken);

        return SettingsGetMethod.ToSnapshot(updated, selector.EffectiveModelId(updated.Transcription), extras);
    }

    /// <summary>Settings › Transcription: each field present replaces the stored one; <c>null</c> keeps it.</summary>
    internal static TranscriptionSettings Merge(TranscriptionSettings current, TranscriptionSettingsPatch patch) =>
        current with
        {
            Auto = patch.Auto ?? current.Auto,
            Timing = patch.Timing ?? current.Timing,
            PauseWhenBusy = patch.PauseWhenBusy ?? current.PauseWhenBusy,
            ModelId = patch.ModelId ?? current.ModelId,
            CpuFallbackModelId = patch.CpuFallbackModelId ?? current.CpuFallbackModelId,
            Language = patch.Language ?? current.Language,
            KeepWordTimestamps = patch.KeepWordTimestamps ?? current.KeepWordTimestamps,
            LowConfidenceThreshold = patch.LowConfidenceThreshold ?? current.LowConfidenceThreshold,
        };

    /// <summary>Settings › Speakers; <c>expectedSpeakers</c> is the string <c>auto</c> or a whole number.</summary>
    internal static SpeakerSettings Merge(SpeakerSettings current, SpeakersSettingsPatch patch)
    {
        var expected = current.ExpectedSpeakers;
        if (patch.ExpectedSpeakers is { } element)
        {
            expected = element.ValueKind switch
            {
                JsonValueKind.String when element.GetString() == "auto" => null,
                JsonValueKind.Number when element.TryGetInt32(out var n) => n,
                _ => throw new BridgeException(InvalidValueCode, $"Expected speakers must be \"auto\" or a whole number from 1 to {SpeakerSettings.MaxExpectedSpeakers}. Nothing was changed."),
            };
        }

        return current with
        {
            Identify = patch.Identify ?? current.Identify,
            ExpectedSpeakers = expected,
            RememberRenamed = patch.RememberRenamed ?? current.RememberRenamed,
            EmbeddingModelId = patch.EmbeddingModelId ?? current.EmbeddingModelId,
        };
    }

    internal static HistorySettings Merge(HistorySettings current, HistorySettingsPatch patch) =>
        current with
        {
            KeepVersions = patch.KeepVersions ?? current.KeepVersions,
            KeepDays = patch.KeepDays ?? current.KeepDays,
        };

    /// <summary>
    /// A model id sent in <c>settings.set</c> must name a catalog model of the right kind that is installed (BRIDGE.md M2
    /// clarification 7); the error names the field. An id equal to the one in effect is accepted as it is, so a UI
    /// that sends the whole block does not fail on a model it did not change.
    /// </summary>
    private void ValidateModels(TranscriptionSettingsPatch? transcription, SpeakersSettingsPatch? speakers)
    {
        var current = store.Current;
        Check("transcription.modelId", transcription?.ModelId, selector.EffectiveModelId(current.Transcription), ModelKinds.Transcription, null, "Settings › Transcription");
        Check("transcription.cpuFallbackModelId", transcription?.CpuFallbackModelId, current.Transcription.CpuFallbackModelId, ModelKinds.Transcription, null, "Settings › Transcription");
        Check("speakers.embeddingModelId", speakers?.EmbeddingModelId, current.Speakers.EmbeddingModelId, ModelKinds.Speakers, ModelRoles.Embedding, "Settings › Speakers");

        void Check(string field, string? id, string inEffect, string kind, string? role, string where)
        {
            if (id is null || id == inEffect)
            {
                return;
            }

            var entry = selector.Catalog.Find(id);
            if (entry is null || entry.Kind != kind || (role is not null && entry.Role != role))
            {
                throw new BridgeException(InvalidValueCode, $"{field}: there is no such model called '{id}'. Choose one of the models listed in {where}. Nothing was changed.", field);
            }

            if (!models.IsInstalled(id))
            {
                throw new BridgeException(InvalidValueCode, $"{field}: {entry.Name} is not installed, so it can't be chosen yet. Install it in {where} first. Nothing was changed.", field);
            }
        }
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
