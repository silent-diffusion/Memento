namespace Memento.Generation.Generation;

/// <summary>What one request of a pass is, for the Live output list: its step and a title.</summary>
/// <param name="Step"><c>map</c> or <c>verify</c> (<see cref="GenerationOutputFeed"/>).</param>
/// <param name="Title">"Decisions and action items · segment 1 of 2".</param>
public sealed record OutputPass(string Step, string Title);
