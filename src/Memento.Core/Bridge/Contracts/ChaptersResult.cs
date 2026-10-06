namespace Memento.Core.Bridge.Contracts;

/// <summary>Every chapter of the recording after the change, ordered by time.</summary>
public sealed record ChaptersResult(IReadOnlyList<Chapter> Chapters);
