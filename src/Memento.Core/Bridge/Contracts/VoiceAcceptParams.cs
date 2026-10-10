namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>voices.acceptMatch</c> ("Use name").</summary>
public sealed record VoiceAcceptParams
{
    public required string RecordingId { get; init; }

    public required string SpeakerId { get; init; }

    public required string VoiceId { get; init; }
}
