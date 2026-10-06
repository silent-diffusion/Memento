using System.Text.Json;
using Memento.Core.Recording.Simulation;

namespace Memento.Core.Tests.Fakes;

/// <summary>Drives recordings through the bridge for tests.</summary>
internal static class TestRecordings
{
    public static readonly string Mic = SimulatedAudioSourceProvider.Microphone.Id;
    public static readonly string SystemAudio = SimulatedAudioSourceProvider.SystemAudio.Id;
    public static readonly string App = SimulatedAudioSourceProvider.MeetingApp.Id;

    public static async Task<(string SessionId, string RecordingId)> StartAsync(this BridgeTestHost host, string title, params string[] sourceIds)
    {
        var result = await host.ResultAsync(
            "recording.start",
            JsonSerializer.Serialize(new { title, type = "meeting", sourceIds }));
        return (result.GetProperty("sessionId").GetString()!, result.GetProperty("recordingId").GetString()!);
    }

    /// <summary>Records <paramref name="seconds"/> of simulated audio and waits for finalize.</summary>
    public static async Task<string> RecordAsync(this BridgeTestHost host, string title, double seconds, params string[] sourceIds)
    {
        var (sessionId, recordingId) = await host.StartAsync(title, sourceIds.Length == 0 ? [Mic] : sourceIds);
        host.Session.Advance(TimeSpan.FromSeconds(seconds));
        await host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await host.Recordings.WhenIdleAsync();
        return recordingId;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(10);
        }
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!await condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(10);
        }
    }
}
