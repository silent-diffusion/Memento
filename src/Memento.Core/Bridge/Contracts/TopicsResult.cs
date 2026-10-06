namespace Memento.Core.Bridge.Contracts;

/// <summary>Every topic of the recording after the change.</summary>
public sealed record TopicsResult(IReadOnlyList<Topic> Topics);
