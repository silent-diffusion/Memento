using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Tests.Fakes;

/// <summary>Synthetic transcripts.</summary>
internal static class TranscriptFixtures
{
    public static TranscriptSegment Segment(string id, double start, double end, string text, string? speaker = null, string track = "mic") =>
        new(id, start, end, track, speaker, speaker is null ? null : 0.9, text, 0.8, WordAligner.Realign(text, start, end).Select(w => w with { C = 0.8 }).ToList(), null);

    public static TranscriptDocument Document(params TranscriptSegment[] segments) => new()
    {
        Language = "en",
        Engine = new TranscriptEngineInfo("whisper.cpp", "whisper-small", "CPU", "1.9.1", 1000),
        Segments = segments,
        Speakers = TranscriptSpeakers.WithTalkTime(
            segments.Select(s => s.Speaker).OfType<string>().Distinct().Select((id, i) => new Speaker(id, TranscriptSpeakers.DefaultName(i + 1), false, TranscriptSpeakers.ColorFor(i), 0)).ToList(),
            segments),
        Version = 0,
    };

    /// <summary>Three lines by two speakers.</summary>
    public static TranscriptDocument Meeting() => Document(
        Segment("s0001", 0, 4, "Welcome everyone to the planning meeting.", "spk1"),
        Segment("s0002", 4.5, 9, "Thanks. Let's review the budget first.", "spk2"),
        Segment("s0003", 9.5, 15, "The budget for the third quarter is approved.", "spk1"));
}
