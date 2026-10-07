using System.Text.Json;
using Memento.Transcription.Words;

namespace Memento.Transcription.Tests;

/// <summary>Reads the synthetic token dumps in <c>fixtures/</c> (shaped like Whisper.net's SegmentData).</summary>
internal static class TokenFixtures
{
    public static List<RawSegment> Load(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name)));
        return document.RootElement.EnumerateArray().Select(s => new RawSegment(
            s.GetProperty("start").GetDouble(),
            s.GetProperty("end").GetDouble(),
            s.GetProperty("text").GetString()!,
            s.GetProperty("minProbability").GetDouble(),
            s.GetProperty("tokens").EnumerateArray().Select(t => new TokenInfo(
                t.GetProperty("text").GetString()!,
                t.GetProperty("start").GetDouble(),
                t.GetProperty("end").GetDouble(),
                t.GetProperty("p").GetDouble())).ToList())).ToList();
    }
}
