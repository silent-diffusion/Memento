using System.Text.Json;
using Memento.Core.Bridge;
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

    /// <summary>
    /// Waits until the coordinator has written a checkpoint into <c>recording.state.json</c>, by waiting for the footer
    /// event it posts after that write. Never poll the state file instead: Windows refuses to replace a file another
    /// handle has open (even one opened with <see cref="FileShare.Delete"/>), so a test reading it in a loop can make
    /// the coordinator's atomic write run out of retries and drop the checkpoint, most often on a slow machine.
    /// </summary>
    public static Task WaitForCheckpointWrittenAsync(this BridgeTestHost host) =>
        host.Sink.WaitForAsync(
            BridgeEventNames.FooterStatus,
            p => p.GetProperty("recording").GetProperty("lastCheckpointAt").ValueKind == JsonValueKind.String);

    /// <summary>
    /// Waits for a <c>processing.progress</c> event in which <paramref name="stage"/> of the recording matches
    /// <paramref name="match"/> (given the stage's JSON: <c>stage</c>, <c>state</c>, <c>percent</c>, <c>label</c>).
    /// Persisted statuses are published after <c>project.json</c> is written. Use this rather than polling the
    /// manifest, for the reason given at <see cref="WaitForCheckpointWrittenAsync"/>: a reader holding the file
    /// open can make the stage's own write fail.
    /// </summary>
    public static Task WaitForStageAsync(this BridgeTestHost host, string recordingId, string stage, Func<JsonElement, bool> match) =>
        host.Sink.WaitForAsync(
            BridgeEventNames.ProcessingProgress,
            p => p.GetProperty("recordingId").GetString() == recordingId
                && p.GetProperty("stages").EnumerateArray().Any(s => s.GetProperty("stage").GetString() == stage && match(s)));

    /// <summary><see cref="WaitForStageAsync"/> for a stage whose label reads <paramref name="label"/>.</summary>
    public static Task WaitForStageLabelAsync(this BridgeTestHost host, string recordingId, string stage, string label) =>
        host.WaitForStageAsync(recordingId, stage, s => s.GetProperty("label").GetString() == label);

    public static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = Patience.CeilingMs)
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

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, int timeoutMs = Patience.CeilingMs)
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
