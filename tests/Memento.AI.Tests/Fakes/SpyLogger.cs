using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Memento.AI.Tests.Fakes;

/// <summary>Captures every log line with its structured values and exception, for "no key in logs" assertions.</summary>
internal sealed class SpyLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public string All => string.Join("\n", _lines);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(" ", pairs.Select(p => p.Key + "=" + p.Value))
            : string.Empty;
        _lines.Enqueue($"{logLevel}: {formatter(state, exception)} | {values} | {exception}");
    }
}
