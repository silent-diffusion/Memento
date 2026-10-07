namespace Memento.Core.Transcripts;

/// <summary>Why a transcript was written: the <c>reason</c> of <c>transcript.changed</c> and of each kept version.</summary>
public static class TranscriptChangeReasons
{
    public const string Transcribed = "transcribed";
    public const string Retranscribed = "retranscribed";
    public const string Edited = "edited";
    public const string Speakers = "speakers";
    public const string Restored = "restored";
    public const string Topics = "topics";
}
