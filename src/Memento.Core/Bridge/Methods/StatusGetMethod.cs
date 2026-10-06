using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Status;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>status.get</c>: the status footer now (also pushed as <c>status.footer</c>).</summary>
public sealed class StatusGetMethod(FooterStatusService footer) : BridgeMethod<EmptyParams, FooterStatusPayload>
{
    public override string Name => BridgeMethodNames.StatusGet;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<FooterStatusPayload> ResultTypeInfo => BridgeJsonContext.Default.FooterStatusPayload;

    public override Task<FooterStatusPayload> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        return Task.FromResult(footer.Compute());
    }
}
