using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using static Memento.Core.Tests.Fakes.TestRecordings;
using static Memento.Core.Tests.Fakes.TranscriptFixtures;

namespace Memento.Core.Tests.Transcripts;

/// <summary><c>transcript.sameVoice</c> (2.6.0) through the bridge: the other lines heard as one voice, from voices.json.</summary>
public sealed class SameVoiceMethodTests : IDisposable
{
    private static readonly string[] First = ["s0001"];
    private static readonly string[] Second = ["s0002"];
    private static readonly string[] Third = ["s0003"];
    private static readonly string[] FirstAndThird = ["s0001", "s0003"];
    private static readonly string[] FirstAndSecond = ["s0001", "s0002"];
    private static readonly string[] FirstAndUnknown = ["s0001", "s0099"];
    private static readonly string[] None = [];

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>The Meeting fixture: s0001 and s0003 are Speaker 1's, s0002 Speaker 2's; one voice won s0001 and s0003.</summary>
    private static VoicesDocument Voices() => new(
        VoicesDocument.CurrentSchemaVersion,
        "nemo-titanet-small",
        "sig",
        [],
        [
            new VoiceCluster("mic", 0, [1f, 0f], 10, ["s0003", "s0001"]),
            new VoiceCluster("mic", 1, [0f, 1f], 4, ["s0002"]),
        ]);

    private async Task<string> RecordingAsync(bool withVoices)
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);
        await _host.Get<TranscriptWriter>().UpdateAsync(id, TranscriptChangeReasons.Transcribed, _ => Meeting(), CancellationToken.None);
        if (withVoices)
        {
            await _host.Transcripts.SaveVoicesAsync(id, Voices(), CancellationToken.None);
        }

        return id;
    }

    private Task<JsonElement> ResultAsync(object parameters) => _host.ResultAsync("transcript.sameVoice", JsonSerializer.Serialize(parameters));

    private async Task<(string Code, string? Detail)> ErrorAsync(object parameters)
    {
        var response = await _host.CallAsync("transcript.sameVoice", JsonSerializer.Serialize(parameters));
        var error = response.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }

    private static string[] Ids(JsonElement result) => result.GetProperty("segmentIds").EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task TheOtherLinesOfTheVoiceComeBackWithoutTheAskedOne()
    {
        var id = await RecordingAsync(withVoices: true);
        var before = (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Version;

        Assert.Equal(["s0003"], Ids(await ResultAsync(new { recordingId = id, segmentIds = First })));
        Assert.Equal(["s0001"], Ids(await ResultAsync(new { recordingId = id, segmentIds = Third })));
        Assert.Empty(Ids(await ResultAsync(new { recordingId = id, segmentIds = Second })));
        // Asking for both lines of the voice leaves nothing; asking across voices gives the union.
        Assert.Empty(Ids(await ResultAsync(new { recordingId = id, segmentIds = FirstAndThird })));
        Assert.Equal(["s0003"], Ids(await ResultAsync(new { recordingId = id, segmentIds = FirstAndSecond })));
        Assert.Equal(before, (await _host.Transcripts.LoadAsync(id, CancellationToken.None))!.Version);
    }

    [Fact]
    public async Task ALineMovedSinceStillBelongsToItsVoice()
    {
        var id = await RecordingAsync(withVoices: true);
        await _host.ResultAsync("transcript.setSegmentSpeaker", JsonSerializer.Serialize(new { recordingId = id, segmentId = "s0003", speakerId = "spk2" }));

        Assert.Equal(["s0003"], Ids(await ResultAsync(new { recordingId = id, segmentIds = First })));
    }

    [Fact]
    public async Task WithoutVoicesNothingSoundsTheSame()
    {
        var id = await RecordingAsync(withVoices: false);

        Assert.Empty(Ids(await ResultAsync(new { recordingId = id, segmentIds = First })));
    }

    [Fact]
    public async Task RefusalsNameWhatIsWrong()
    {
        var id = await RecordingAsync(withVoices: true);

        Assert.Equal(("bridge.invalidParams", null), await ErrorAsync(new { recordingId = id, segmentIds = None }));
        Assert.Equal(("transcript.segmentNotFound", "s0099"), await ErrorAsync(new { recordingId = id, segmentIds = FirstAndUnknown }));
        Assert.Equal(("project.notFound", "20260101-000000-nope"), await ErrorAsync(new { recordingId = "20260101-000000-nope", segmentIds = First }));
    }

    [Fact]
    public async Task NoTranscriptYetIsSaidSo()
    {
        var id = await _host.RecordAsync("Planning", 1, Mic);

        var (code, _) = await ErrorAsync(new { recordingId = id, segmentIds = First });
        Assert.Equal("transcript.none", code);
    }
}
