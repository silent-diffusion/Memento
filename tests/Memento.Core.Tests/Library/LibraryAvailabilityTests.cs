using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Library;
using Memento.Core.Tests.Fakes;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Library;

/// <summary>A library on a drive that is not connected, or a folder that went away (H1 storage).</summary>
public sealed class LibraryAvailabilityTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private string MissingLibrary => Path.Combine(_host.Root, "USB stick", "Memento Library");

    [Fact]
    public async Task AMissingLibraryIsReportedAndNeverCreatedEmpty()
    {
        _host.Library.Root = MissingLibrary;

        var opened = await _host.Get<LibraryOpener>().OpenAsync(CancellationToken.None);

        Assert.False(opened);
        Assert.False(Directory.Exists(MissingLibrary));
        var list = await _host.CallAsync("library.list", "{}");
        var error = list.GetProperty("error");
        Assert.Equal(DomainErrorCodes.LibraryUnavailable, error.GetProperty("code").GetString());
        Assert.Equal(
            $"The library folder {MissingLibrary} is not there: it may have been moved, renamed or deleted. Memento cannot show it until it is back. The recordings in it are not affected. Reconnect the drive or put the folder back, or choose another location in Settings › General.",
            error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ARecordingDoesNotStartWhileTheLibraryIsMissing()
    {
        _host.Library.Root = MissingLibrary;
        await _host.Get<LibraryOpener>().OpenAsync(CancellationToken.None);

        var start = await _host.CallAsync("recording.start", JsonSerializer.Serialize(new { title = "Board", type = "meeting", sourceIds = new[] { Mic } }));

        var error = start.GetProperty("error");
        Assert.Equal(DomainErrorCodes.LibraryUnavailable, error.GetProperty("code").GetString());
        Assert.Contains("The recording did not start and nothing was recorded.", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(MissingLibrary));
    }

    [Fact]
    public async Task WhenTheFolderIsBackTheLibraryOpensAndRecordingWorks()
    {
        _host.Library.Root = MissingLibrary;
        var opener = _host.Get<LibraryOpener>();
        await opener.OpenAsync(CancellationToken.None);
        var openedAgain = 0;
        opener.Opened += (_, _) => openedAgain++;

        Directory.CreateDirectory(MissingLibrary); // the drive is plugged in again

        var list = await _host.ResultAsync("library.list", "{}");
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, openedAgain);
        Assert.Null(_host.Get<LibraryAvailability>().Unavailable);
        var id = await _host.RecordAsync("Back again", 1, Mic);
        Assert.True(File.Exists(Path.Combine(MissingLibrary, "projects", id, "project.json")));
    }

    [Fact]
    public async Task OnlyLibraryListExplainsAMissingLibraryTheOtherStartupCallsAnswerEmpty()
    {
        _host.Library.Root = MissingLibrary;
        await _host.Get<LibraryOpener>().OpenAsync(CancellationToken.None);

        var recovery = await _host.ResultAsync("recovery.list", "{}");
        var processing = await _host.ResultAsync("library.processing", "{}");

        Assert.Equal(0, recovery.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, processing.GetProperty("current").ValueKind);
    }

    [Fact]
    public async Task StoppingWhenTheLibraryFolderCannotBeReachedEndsTheSessionAndSaysSo()
    {
        Directory.CreateDirectory(_host.Library.Root);
        var (sessionId, recordingId) = await _host.StartAsync("Drive pulled", Mic);
        _host.Session.Advance(TimeSpan.FromSeconds(3));
        var original = _host.Library.Root;
        _host.Library.Root = MissingLibrary; // the drive letter went away under the recording

        var stop = await _host.CallAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));

        var error = stop.GetProperty("error");
        Assert.Equal(DomainErrorCodes.LibraryUnavailable, error.GetProperty("code").GetString());
        Assert.StartsWith("The recording stopped at 0:03, but its folder", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Memento finishes it the next time it starts", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        var state = _host.Sink.Payloads(BridgeEventNames.RecordingState).Last();
        Assert.Equal("stopped", state.GetProperty("state").GetString());
        Assert.Null(_host.Recordings.Current);

        // Back again: the state file is still there, so the next launch recovers it.
        _host.Library.Root = original;
        Assert.True(_host.Store.HasRecordingState(recordingId));
        var recovered = await _host.Recovery.RunAsync(CancellationToken.None);
        Assert.Equal([recordingId], recovered);
    }

    [Fact]
    public void AMissingDriveIsNamed()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = Enumerable.Range('D', 23).Select(c => (char)c).First(c => !used.Contains(c));

        var words = LibraryAvailability.Describe($@"{free}:\Memento Library");

        Assert.Equal($@"The library folder {free}:\Memento Library is not available because drive {free}: is not connected.", words);
    }
}
