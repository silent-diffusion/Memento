using System.Text.Json;

namespace Memento.Transcription.Tests;

/// <summary>The synthetic recording with every line at a known time (<c>tools/e2e/sync-fixture.ps1</c>).</summary>
internal static class SyncFixture
{
    /// <summary><c>MEMENTO_SYNC_WAV</c>, when it and its truth file exist.</summary>
    public static string? Wav => Environment.GetEnvironmentVariable("MEMENTO_SYNC_WAV") is { Length: > 0 } wav && File.Exists(wav) && File.Exists(wav + ".json") ? wav : null;

    public static string ModelPath => Path.Combine(
        WorkerBuild.ModelsRoot,
        "whisper",
        Environment.GetEnvironmentVariable("MEMENTO_SYNC_MODEL") is { Length: > 0 } model ? model : "ggml-large-v3-turbo.bin");

    /// <summary>Each line's text and where its voice really starts and ends, in seconds.</summary>
    public static IReadOnlyList<(string Text, double Start, double End)> Lines()
    {
        using var truth = JsonDocument.Parse(File.ReadAllText(Wav! + ".json"));
        return truth.RootElement.GetProperty("lines").EnumerateArray()
            .Select(l => (l.GetProperty("text").GetString()!, l.GetProperty("start").GetDouble(), l.GetProperty("end").GetDouble()))
            .ToList();
    }
}
