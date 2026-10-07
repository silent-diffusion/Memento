namespace Memento.Documents.Render;

/// <summary>A timestamp turned into a footnote in print and Word: number, moment and the note text.</summary>
public sealed record Footnote(int Number, double T, string Display, string Text);
