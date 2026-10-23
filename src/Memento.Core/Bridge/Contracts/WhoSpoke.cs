using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Who spoke in one recording, as the user said (Record's details or Review's People pane): how many people and/or
/// their names. The speakers stage clusters to <see cref="Count"/> (or to as many as <see cref="Names"/> when no count is
/// given) and names the speakers it finds after <see cref="Names"/>, in order of first appearance, except where a
/// "Speaking now" mark (<see cref="Marks"/>, 2.6.0) says who was talking at a moment: that voice gets that name first.
/// Settings › Speakers › Expected speakers applies to recordings without their own value.
/// </summary>
/// <param name="Count">1–20, or <c>null</c> to follow <see cref="Names"/> or Settings.</param>
/// <param name="Names">The known speakers, at most 20, each 1–100 characters, no repeats (ignoring case).</param>
/// <param name="Marks">Speaking now marks, at most 200, each naming one of <see cref="Names"/>; empty before 2.6.0.</param>
public sealed record WhoSpoke(int? Count, IReadOnlyList<string> Names, IReadOnlyList<SpokeMark>? Marks = null)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    public IReadOnlyList<string> Names { get; set; } = Names ?? [];

    [JsonPropertyOrder(1)]
    public IReadOnlyList<SpokeMark> Marks { get; set; } = Marks ?? [];

    public const int MaxCount = 20;

    public const int MaxNames = 20;

    public const int MaxNameLength = 100;

    public const int MaxMarks = 200;

    public static WhoSpoke Unknown { get; } = new(null, []);

    /// <summary>The count the speakers stage clusters to: the count, else the number of names, else <c>null</c>.</summary>
    [JsonIgnore]
    public int? EffectiveCount => Count ?? (Names.Count > 0 ? Names.Count : null);
}
