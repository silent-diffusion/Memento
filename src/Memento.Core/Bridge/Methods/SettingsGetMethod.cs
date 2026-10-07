using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>settings.get</c> → <see cref="SettingsSnapshot"/>. An unset transcription model reads as the recommended one for this PC.</summary>
public sealed class SettingsGetMethod(ISettingsStore store, EngineSelector selector) : BridgeMethod<EmptyParams, SettingsSnapshot>
{
    public override string Name => BridgeMethodNames.SettingsGet;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<SettingsSnapshot> ResultTypeInfo => BridgeJsonContext.Default.SettingsSnapshot;

    public override Task<SettingsSnapshot> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(ToSnapshot(store.Current, selector.EffectiveModelId(store.Current.Transcription)));

    internal static SettingsSnapshot ToSnapshot(AppSettings settings, string effectiveModelId)
    {
        var recording = settings.Recording;
        var storage = recording.Storage;
        var transcription = settings.Transcription;
        var speakers = settings.Speakers;
        return new SettingsSnapshot(
            settings.Theme,
            settings.EffectiveLibraryPath,
            settings.ListDensity,
            new RecordingSettingsSnapshot(
                recording.DefaultType,
                recording.DefaultSourceIds,
                recording.KeepSeparateTracks,
                new StorageSettingsSnapshot(storage.Codec, storage.IsLossy ? storage.BitrateKbps : null, storage.DownmixMono, storage.KeepOnlyMix),
                recording.CheckpointSeconds,
                recording.LowSpaceGb),
            new TranscriptionSettingsSnapshot(
                transcription.Auto,
                transcription.Timing,
                transcription.PauseWhenBusy,
                effectiveModelId,
                transcription.CpuFallbackModelId,
                transcription.Language,
                transcription.KeepWordTimestamps,
                transcription.LowConfidenceThreshold),
            new SpeakersSettingsSnapshot(
                speakers.Identify,
                ExpectedSpeakersElement(speakers.ExpectedSpeakers),
                speakers.RememberRenamed,
                speakers.EmbeddingModelId),
            new HistorySettingsSnapshot(settings.History.KeepVersions, settings.History.KeepDays));
    }

    /// <summary>The bridge form of the expected speaker count: the string <c>auto</c> or a number.</summary>
    internal static JsonElement ExpectedSpeakersElement(int? expected)
    {
        using var document = JsonDocument.Parse(expected is { } n ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "\"auto\"");
        return document.RootElement.Clone();
    }
}
