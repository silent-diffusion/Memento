namespace Memento.Core.Tests.Fakes;

/// <summary>
/// How long a test waits for asynchronous work before it fails. A ceiling, never an expectation: on a slow CI runner
/// with a starved thread pool, work that takes milliseconds here can take seconds, so tests wait for a signal or a
/// condition and only give up after this. Never assert that something finished within a wall-clock time.
/// </summary>
/// <remarks>Linked into the Generation and Transcription test projects with the rest of this folder.</remarks>
internal static class Patience
{
    public const int CeilingMs = 30_000;

    public static readonly TimeSpan Ceiling = TimeSpan.FromMilliseconds(CeilingMs);
}
