namespace Memento.Audio.Tests;

/// <summary>
/// How long a test waits for asynchronous work before it fails. A ceiling, never an expectation: on a slow CI runner
/// with a starved thread pool, work that takes milliseconds here can take seconds, so tests wait for a signal or a
/// condition and only give up after this. Never assert that something finished within a wall-clock time.
/// </summary>
internal static class Patience
{
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);
}
