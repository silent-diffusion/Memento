using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Bridge;

/// <summary>A recording's own speaker count and names (<c>details.whoSpoke</c>) through <c>project.updateDetails</c> and <c>project.get</c>.</summary>
public sealed class WhoSpokeMethodTests : IDisposable
{
    private static readonly string[] RepeatedNames = [" Avery Stone ", "Rowan Hale", "avery stone"];
    private static readonly string[] OneName = ["Sam"];

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string? ErrorCode(JsonElement response) =>
        response.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    [Fact]
    public async Task ANewRecordingLeavesItToSettings()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);

        var project = await _host.ResultAsync("project.get", Json(new { recordingId }));

        Assert.Equal("""{"count":null,"names":[],"marks":[],"added":[]}""", project.GetProperty("details").GetProperty("whoSpoke").GetRawText());
    }

    [Fact]
    public async Task CountAndNamesAreSavedTrimmedWithoutRepeatsAndReplaceWhole()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);

        await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = 3, names = RepeatedNames } } }));
        var kept = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { purpose = "Plan Q4" } }));
        var replaced = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = OneName } } }));

        Assert.Equal("""{"count":3,"names":["Avery Stone","Rowan Hale"],"marks":[],"added":[]}""", kept.GetProperty("details").GetProperty("whoSpoke").GetRawText());
        Assert.Equal("""{"count":null,"names":["Sam"],"marks":[],"added":[]}""", replaced.GetProperty("details").GetProperty("whoSpoke").GetRawText());
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(1, manifest.Details.WhoSpoke.EffectiveCount);
        Assert.Equal("Plan Q4", manifest.Details.Purpose);
    }

    [Theory]
    [InlineData(0, "Ana")]
    [InlineData(21, "Ana")]
    [InlineData(2, " ")]
    public async Task OutOfRangeValuesAreRefusedAndNothingChanges(int count, string name)
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);

        var response = await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count, names = new[] { name } }, purpose = "changed" } }));

        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(response));
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Null(manifest.Details.WhoSpoke.Count);
        Assert.Equal(string.Empty, manifest.Details.Purpose);
    }

    [Fact]
    public async Task MoreThanTwentyNamesOrOverlongNamesAreRefused()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);
        var many = Enumerable.Range(1, 21).Select(n => $"Person {n}").ToArray();

        var tooMany = await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = many } } }));
        var tooLong = await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = new[] { new string('a', 101) } } } }));

        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(tooMany));
        Assert.Contains("At most 20 speakers", tooMany.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(tooLong));
    }

    [Fact]
    public async Task SpeakingNowMarksAreKeptSpelledAsTheNameAndSurviveOtherEdits()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);
        var marks = new[] { new { name = "rowan hale ", atMs = 12_500L }, new { name = "Avery Stone", atMs = 0L } };

        var saved = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = RepeatedNames, marks } } }));
        var kept = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { purpose = "Plan Q4" } }));

        Assert.Equal("""{"count":null,"names":["Avery Stone","Rowan Hale"],"marks":[{"name":"Rowan Hale","atMs":12500},{"name":"Avery Stone","atMs":0}],"added":[]}""", saved.GetProperty("details").GetProperty("whoSpoke").GetRawText());
        Assert.Equal(saved.GetProperty("details").GetProperty("whoSpoke").GetRawText(), kept.GetProperty("details").GetProperty("whoSpoke").GetRawText());
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal([("Rowan Hale", 12_500L), ("Avery Stone", 0L)], manifest.Details.WhoSpoke.Marks.Select(m => (m.Name, m.AtMs)));
    }

    [Fact]
    public async Task AMarkForANameNotListedOrBeforeTheStartOrTooManyMarksAreRefusedAndNothingChanges()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);
        var tooMany = Enumerable.Range(0, 201).Select(n => new { name = "Sam", atMs = (long)n * 1000 }).ToArray();

        var unknown = await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = OneName, marks = new[] { new { name = "Ana", atMs = 5L } } }, purpose = "changed" } }));
        var negative = await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = OneName, marks = new[] { new { name = "Sam", atMs = -1L } } } } }));
        var many = await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = OneName, marks = tooMany } } }));

        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(unknown));
        Assert.Equal("A Speaking now mark needs the name of one of the speakers listed; \"Ana\" is not one of them.", unknown.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(negative));
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(many));
        Assert.Contains("At most 200 Speaking now marks", many.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Empty(manifest.Details.WhoSpoke.Names);
        Assert.Empty(manifest.Details.WhoSpoke.Marks);
        Assert.Equal(string.Empty, manifest.Details.Purpose);
    }

    [Fact]
    public async Task AddedTimesAreKeptOncePerListedNameAndDroppedWithTheName()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);
        var added = new[]
        {
            new { name = "rowan hale", atMs = 61_000L, at = "2026-01-05T10:01:01+00:00" },
            new { name = "Rowan Hale", atMs = 90_000L, at = "2026-01-05T10:01:30+00:00" },
            new { name = "Gone", atMs = 5L, at = "2026-01-05T10:00:05+00:00" },
        };

        var saved = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = RepeatedNames, added } } }));
        var negative = await _host.CallAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = OneName, added = new[] { new { name = "Sam", atMs = -1L, at = "2026-01-05T10:00:00+00:00" } } } } }));

        var kept = Assert.Single(saved.GetProperty("details").GetProperty("whoSpoke").GetProperty("added").EnumerateArray());
        Assert.Equal("Rowan Hale", kept.GetProperty("name").GetString());
        Assert.Equal(61_000, kept.GetProperty("atMs").GetInt64());
        Assert.Equal(BridgeErrorCodes.InvalidParams, ErrorCode(negative));
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 10, 1, 1, TimeSpan.Zero), Assert.Single(manifest.Details.WhoSpoke.Added).At);
    }
}
