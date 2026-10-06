using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>ui.ready</c>: the page has mounted and painted its first state.</summary>
public sealed class UiReadyMethod(IUiLifecycle lifecycle) : BridgeMethod<EmptyParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.UiReady;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        lifecycle.NotifyReady();
        return Task.FromResult(new EmptyResult());
    }
}
