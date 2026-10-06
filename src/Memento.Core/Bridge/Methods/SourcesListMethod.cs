using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>sources.list</c>: re-enumerates the audio sources on every call. Video is not in 1.0.</summary>
public sealed class SourcesListMethod(IAudioSourceProvider sources) : BridgeMethod<EmptyParams, SourcesListResult>
{
    public override string Name => BridgeMethodNames.SourcesList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<SourcesListResult> ResultTypeInfo => BridgeJsonContext.Default.SourcesListResult;

    public override async Task<SourcesListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        return new SourcesListResult(await sources.ListAsync(cancellationToken), VideoAvailable: false);
    }
}
