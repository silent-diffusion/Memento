using System.Text.Json;

namespace Memento.Core.Bridge;

/// <summary>Untyped view of a bridge method, used by <see cref="BridgeRouter"/> for dispatch.</summary>
public interface IBridgeHandler
{
    string Name { get; }

    /// <summary>Deserializes <paramref name="parameters"/>, runs the method and serializes its result.</summary>
    /// <exception cref="BridgeException">The parameters are invalid or the method reports a specific failure.</exception>
    Task<JsonElement> HandleAsync(JsonElement parameters, CancellationToken cancellationToken);
}
