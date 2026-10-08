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

        Assert.Equal("""{"count":null,"names":[]}""", project.GetProperty("details").GetProperty("whoSpoke").GetRawText());
    }

    [Fact]
    public async Task CountAndNamesAreSavedTrimmedWithoutRepeatsAndReplaceWhole()
    {
        var recordingId = await _host.RecordAsync("Kickoff", 0.5);

        await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = 3, names = RepeatedNames } } }));
        var kept = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { purpose = "Plan Q4" } }));
        var replaced = await _host.ResultAsync("project.updateDetails", Json(new { recordingId, details = new { whoSpoke = new { count = (int?)null, names = OneName } } }));

        Assert.Equal("""{"count":3,"names":["Avery Stone","Rowan Hale"]}""", kept.GetProperty("details").GetProperty("whoSpoke").GetRawText());
        Assert.Equal("""{"count":null,"names":["Sam"]}""", replaced.GetProperty("details").GetProperty("whoSpoke").GetRawText());
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
}
