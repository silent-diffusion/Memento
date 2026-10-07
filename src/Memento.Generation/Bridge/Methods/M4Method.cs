using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;

namespace Memento.Generation.Bridge.Methods;

/// <summary>The M4 bridge methods' common helpers on top of <see cref="BridgeMethod{TParams,TResult}"/>.</summary>
public abstract class M4Method<TParams, TResult> : BridgeMethod<TParams, TResult>
{
    private static readonly EmptyResult Empty = new();

    protected static async Task<TResult> Wrap<T>(Task<T> task, Func<T, TResult> map)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(map);
        return map(await task);
    }

    protected static async Task<EmptyResult> Done(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        await task;
        return Empty;
    }

    protected static Task<EmptyResult> Done(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        return Task.FromResult(Empty);
    }

    /// <summary>The template a request carries; it is required.</summary>
    protected static Template Template(Template? template) =>
        template ?? throw M4Errors.Invalid($"'{typeof(TParams).Name}' needs a template.", "template");
}
