namespace Memento.Core.Bridge.Contracts;

/// <summary>The <c>state</c> values of <see cref="LiveTranscriptPayload"/>.</summary>
public static class LiveTranscriptStates
{
    public const string Starting = "starting";
    public const string Listening = "listening";
    public const string Paused = "paused";
    public const string Unavailable = "unavailable";
    public const string Failed = "failed";
}
