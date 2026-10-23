using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Serialization;

namespace Memento.Core.Bridge;

/// <summary>Base class that adapts a typed <see cref="IBridgeMethod{TParams,TResult}"/> to <see cref="IBridgeHandler"/>.</summary>
public abstract class BridgeMethod<TParams, TResult> : IBridgeMethod<TParams, TResult>, IBridgeHandler
{
    public abstract string Name { get; }

    public abstract JsonTypeInfo<TParams> ParamsTypeInfo { get; }

    public abstract JsonTypeInfo<TResult> ResultTypeInfo { get; }

    public abstract Task<TResult> InvokeAsync(TParams parameters, CancellationToken cancellationToken);

    public async Task<JsonElement> HandleAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        TParams? typed;
        try
        {
            typed = parameters.Deserialize(ParamsTypeInfo);
        }
        catch (JsonException ex)
        {
            // JsonException messages name the JSON path and the expected type; they never carry a stack trace.
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"The parameters for '{Name}' are not valid.", ex.Message);
        }

        if (typed is null)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"The parameters for '{Name}' must be an object.");
        }

        // System.Text.Json leaves a list or object null when the page sends null (or [null]) for it, whatever the
        // contract's annotations say (security audit 2, §9); no method is handed one.
        if (NullMembers.FindFirst(typed) is { } missing)
        {
            throw new BridgeException(
                BridgeErrorCodes.InvalidParams,
                $"The parameters for '{Name}' are not valid: '{missing}' is null. Nothing was changed; send a value for it or leave it out.",
                $"'{missing}' cannot be null.");
        }

        var result = await InvokeAsync(typed, cancellationToken);
        return JsonSerializer.SerializeToElement(result, ResultTypeInfo);
    }
}
