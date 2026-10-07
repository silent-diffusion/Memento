using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>providers.list</c>: readiness from Settings, keys, the model manager and free video memory. Nothing is sent.</summary>
public sealed class ProvidersListMethod(ProviderRegistry providers, ISettingsStore settings) : M4Method<EmptyParams, ProvidersListResult>
{
    public override string Name => BridgeMethodNames.ProvidersList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => M4BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<ProvidersListResult> ResultTypeInfo => M4BridgeJsonContext.Default.ProvidersListResult;

    public override Task<ProvidersListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(new ProvidersListResult(providers.List().Select(M4Mapping.ToInfo).ToList(), settings.Current.Ai.Enabled) { DefaultProviderId = settings.Current.Ai.DefaultProviderId });
}
