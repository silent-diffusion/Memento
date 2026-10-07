using Microsoft.Extensions.Logging;

namespace Memento.Worker;

/// <summary>
/// Sends the engine's own log messages (information and above) to the host as <c>log</c> lines, which the host writes to
/// its log. The local engine logs ids, counts and timings only, never prompt or answer text.
/// </summary>
internal sealed class ProtocolLogger<T>(ProtocolWriter output) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (IsEnabled(logLevel))
        {
            output.Log(formatter(state, exception));
        }
    }
}
