namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>voices.remember</c>: the named speaker whose voice to remember under its name.</summary>
public sealed record VoiceRememberParams
{
    public required string RecordingId { get; init; }

    public required string SpeakerId { get; init; }
}
