using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Who spoke in one recording, as the user said (Record's details or Review's People pane): how many people and/or
/// their names. The speakers stage clusters to <see cref="Count"/> (or to as many as <see cref="Names"/> when no count is
/// given) and names the speakers it finds after <see cref="Names"/>, in order of first appearance. Settings › Speakers ›
/// Expected speakers applies to recordings without their own value.
/// </summary>
/// <param name="Count">1–20, or <c>null</c> to follow <see cref="Names"/> or Settings.</param>
/// <param name="Names">The known speakers, at most 20, each 1–100 characters, no repeats (ignoring case).</param>
public sealed record WhoSpoke(int? Count, IReadOnlyList<string> Names)
{
    public const int MaxCount = 20;

    public const int MaxNames = 20;

    public const int MaxNameLength = 100;

    public static WhoSpoke Unknown { get; } = new(null, []);

    /// <summary>The count the speakers stage clusters to: the count, else the number of names, else <c>null</c>.</summary>
    [JsonIgnore]
    public int? EffectiveCount => Count ?? (Names.Count > 0 ? Names.Count : null);
}
