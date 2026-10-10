namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>voices.decline</c> ("Not {name}"); <see cref="Declined"/> false takes it back (Undo).</summary>
public sealed record VoiceDeclineParams
{
    public required string RecordingId { get; init; }

    public required string VoiceId { get; init; }

    public required bool Declined { get; init; }
}
