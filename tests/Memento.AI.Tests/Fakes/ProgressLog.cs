using System.Collections.Concurrent;

namespace Memento.AI.Tests.Fakes;

/// <summary>A synchronous <see cref="IProgress{T}"/> that keeps every report in order.</summary>
internal sealed class ProgressLog<T> : IProgress<T>
{
    private readonly ConcurrentQueue<T> _items = new();

    public IReadOnlyList<T> Items => [.. _items];

    public void Report(T value) => _items.Enqueue(value);
}
