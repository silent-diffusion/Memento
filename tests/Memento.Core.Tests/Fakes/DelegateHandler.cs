using System.Text.Json;
using Memento.Core.Bridge;

namespace Memento.Core.Tests.Fakes;

/// <summary>Minimal <see cref="IBridgeHandler"/> whose behaviour is a delegate.</summary>
internal sealed class DelegateHandler(string name, Func<JsonElement, CancellationToken, Task<JsonElement>> handle) : IBridgeHandler
{
    public string Name { get; } = name;

    public int Calls { get; private set; }

    public Task<JsonElement> HandleAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        Calls++;
        return handle(parameters, cancellationToken);
    }

    public static DelegateHandler Echo(string name) =>
        new(name, (parameters, _) => Task.FromResult(parameters.Clone()));

    public static DelegateHandler Throwing(string name, Exception exception) =>
        new(name, (_, _) => Task.FromException<JsonElement>(exception));
}
