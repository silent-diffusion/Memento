namespace Memento.Generation.Generation;

/// <summary>
/// An <see cref="IProgress{T}"/> that runs its handler on the reporting thread, in the order of the reports
/// (<see cref="Progress{T}"/> posts each report to the thread pool, where a later report can overtake an earlier one).
/// </summary>
public sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
