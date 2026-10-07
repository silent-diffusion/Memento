namespace Memento.Transcription.Words;

/// <summary>One Whisper token as the engine gives it (sub-word text, times relative to the processed audio, probability).</summary>
/// <param name="Text">Token text; a word starts at a token whose text begins with a space.</param>
/// <param name="Start">Seconds from the start of the audio window.</param>
/// <param name="End">Seconds from the start of the audio window.</param>
/// <param name="Probability">0..1.</param>
public sealed record TokenInfo(string Text, double Start, double End, double Probability)
{
    /// <summary>Control tokens (<c>[_BEG_]</c>, <c>[_TT_150]</c>, <c>&lt;|endoftext|&gt;</c>) carry no words.</summary>
    public bool IsSpecial => Text.StartsWith("[_", StringComparison.Ordinal) || Text.StartsWith("<|", StringComparison.Ordinal);
}
