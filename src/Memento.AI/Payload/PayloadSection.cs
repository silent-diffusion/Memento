namespace Memento.AI.Payload;

/// <summary>One rendered part of a payload: its tag, a human title and the exact text sent (tags included).</summary>
/// <param name="Summary">"312 segments, 4 speakers", "6 items".</param>
public sealed record PayloadSection(PayloadSectionKind Kind, string Title, string Summary, string Text);
