namespace Memento.Core.Bridge.Contracts;

/// <summary>A speaker as <c>transcript.restoreSpeaker</c> puts it back: the id, name, colour and renamed flag it had.</summary>
/// <param name="Id">The speaker's id (<c>spk3</c>); kept so later edits that name it still find it.</param>
/// <param name="Name">1–100 characters.</param>
/// <param name="Color">1–4.</param>
/// <param name="Renamed">Whether the name was given by a person.</param>
public sealed record SpeakerRestore(string Id, string Name, int Color, bool Renamed);
