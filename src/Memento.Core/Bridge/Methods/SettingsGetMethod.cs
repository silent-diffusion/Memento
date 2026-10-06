using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>settings.get</c> → <see cref="SettingsSnapshot"/>.</summary>
public sealed class SettingsGetMethod(ISettingsStore store) : BridgeMethod<EmptyParams, SettingsSnapshot>
{
    public override string Name => BridgeMethodNames.SettingsGet;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<SettingsSnapshot> ResultTypeInfo => BridgeJsonContext.Default.SettingsSnapshot;

    public override Task<SettingsSnapshot> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(ToSnapshot(store.Current));

    internal static SettingsSnapshot ToSnapshot(AppSettings settings) =>
        new(settings.Theme, settings.EffectiveLibraryPath, settings.ListDensity);
}
