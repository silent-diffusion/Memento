using System.Globalization;
using System.Text;
using Memento.AI.Payload;

namespace Memento.AI.Tests.Fakes;

/// <summary>Deterministic synthetic meetings (invented speakers and words) for chunker and composer tests.</summary>
internal static class SyntheticTranscript
{
    private static readonly string[] Words =
    [
        "release", "scope", "budget", "the", "we", "should", "ship", "on", "Thursday", "and", "move", "templates", "into",
        "next", "quarter", "because", "testing", "needs", "more", "time", "I", "think", "that", "works", "for", "design",
        "pricing", "page", "toggle", "monthly", "annual", "support", "tickets", "offline", "sync", "conflict", "log", "owner",
        "Friday", "review", "agreed", "decision", "follow", "up", "with", "vendor", "sandbox", "credentials", "draft",
    ];

    public static IReadOnlyList<PayloadSpeaker> Speakers(int count) =>
        Enumerable.Range(1, count).Select(i => new PayloadSpeaker($"spk{i}", $"Speaker {(char)('A' + i - 1)}")).ToList();

    /// <summary>
    /// <paramref name="segments"/> segments of 6–40 words; the speaker changes on about half of them; a chapter every
    /// <paramref name="chapterEvery"/> segments and a topic every <paramref name="topicEvery"/>.
    /// </summary>
    public static (IReadOnlyList<PayloadSegment> Segments, IReadOnlyList<PayloadMarker> Chapters, IReadOnlyList<PayloadMarker> Topics) Create(
        int segments, int speakers = 4, int chapterEvery = 400, int topicEvery = 90, uint seed = 12345)
    {
        var state = seed;
        int Next(int max)
        {
            state = (state * 1664525u) + 1013904223u;
            return (int)((state >> 8) % (uint)max);
        }

        var list = new List<PayloadSegment>(segments);
        var chapters = new List<PayloadMarker>();
        var topics = new List<PayloadMarker>();
        var time = 0.5;
        var speaker = 1;
        for (var i = 0; i < segments; i++)
        {
            if (i > 0 && Next(2) == 0)
            {
                speaker = (speaker % speakers) + 1;
            }

            var count = 6 + Next(35);
            var text = new StringBuilder();
            for (var w = 0; w < count; w++)
            {
                text.Append(w == 0 ? string.Empty : " ").Append(Words[Next(Words.Length)]);
            }

            text.Append(Next(4) == 0 ? "?" : ".");
            var duration = count * 0.4;
            if (i > 0 && i % chapterEvery == 0)
            {
                chapters.Add(new PayloadMarker(time, string.Create(CultureInfo.InvariantCulture, $"Chapter {chapters.Count + 1}")));
            }
            else if (i > 0 && i % topicEvery == 0)
            {
                topics.Add(new PayloadMarker(time, string.Create(CultureInfo.InvariantCulture, $"Topic {topics.Count + 1}")));
            }

            list.Add(new PayloadSegment(string.Create(CultureInfo.InvariantCulture, $"s{i + 1:0000}"), Math.Round(time, 2), Math.Round(time + duration, 2), $"spk{speaker}", text.ToString()));
            time += duration + 0.6;
        }

        return (list, chapters, topics);
    }
}
