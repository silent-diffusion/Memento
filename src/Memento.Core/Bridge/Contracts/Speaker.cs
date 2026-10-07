namespace Memento.Core.Bridge.Contracts;

/// <summary>A speaker of a transcript. Segments refer to it by <see cref="Id"/>, so a rename changes one place.</summary>
/// <param name="Id"><c>spk1</c>, <c>spk2</c>, …</param>
/// <param name="Name">"Speaker 1" until renamed.</param>
/// <param name="Color">1–4, cycling in order of first appearance.</param>
/// <param name="TalkTimeMs">Total length of the segments assigned to this speaker.</param>
public sealed record Speaker(string Id, string Name, bool Renamed, int Color, long TalkTimeMs);
