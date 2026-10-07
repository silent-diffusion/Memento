namespace Memento.Documents.Tests.Support;

/// <summary>One expected agenda item.</summary>
internal sealed record ExpectedItem(string Text, int Level, bool Uncertain, string? Time)
{
    public override string ToString() =>
        new string(' ', Level * 2) + (Uncertain ? "? " : string.Empty) + Text + (Time is null ? string.Empty : " | " + Time);
}
