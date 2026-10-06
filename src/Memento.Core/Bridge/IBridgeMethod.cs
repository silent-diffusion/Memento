using System.Text.Json.Serialization.Metadata;

namespace Memento.Core.Bridge;

/// <summary>A typed host method callable from the UI.</summary>
/// <typeparam name="TParams">Request parameters; deserialized from <c>params</c>.</typeparam>
/// <typeparam name="TResult">Response payload; serialized into <c>result</c>.</typeparam>
public interface IBridgeMethod<TParams, TResult>
{
    /// <summary>Method name, <c>area.verb</c>.</summary>
    string Name { get; }

    JsonTypeInfo<TParams> ParamsTypeInfo { get; }

    JsonTypeInfo<TResult> ResultTypeInfo { get; }

    Task<TResult> InvokeAsync(TParams parameters, CancellationToken cancellationToken);
}
