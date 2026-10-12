using System.Text.Json;
using Memento.Core.Settings;

namespace Memento.Core.Tests.Settings;

/// <summary>
/// A settings.json written by an earlier version lacks the fields added since. Every such field must read as its
/// default, never as null: the page sends whole blocks back on any change, so one null field would make every save
/// fail with "… is not available" (seen in 2.1.0 with the date format and the library view of a 2.0.0 file).
/// </summary>
public sealed class MissingFieldsKeepDefaultsTests
{
    private const string TwoPointZeroFile = """
        {
          "schemaVersion": 2,
          "theme": "dark",
          "general": { "autoUpdate": true },
          "recording": { "defaultType": "lecture", "storage": { "codec": "flac" }, "video": { "frameRate": 30 } },
          "transcription": { "auto": true },
          "speakers": { "identify": true },
          "history": { "keepVersions": true },
          "storage": { }
        }
        """;

    [Fact]
    public void FieldsAddedAfterTheFileWasWrittenReadAsTheirDefaults()
    {
        var settings = JsonSerializer.Deserialize(TwoPointZeroFile, SettingsJsonContext.Default.AppSettings)!;

        Assert.Equal(Memento.Core.Formatting.DateFormats.Default, settings.General.DateFormat);
        Assert.Equal(GeneralSettings.TimelineView, settings.General.LibraryView);
        Assert.Equal("lecture", settings.Recording.DefaultType);
        Assert.Equal("dark", settings.Theme);
        Assert.Equal(Memento.Core.Settings.ListDensity.Comfortable, settings.ListDensity);
        Assert.True(settings.Recording.Storage.EvenLevels);
        Assert.Equal(TranscriptionSettings.TimingAfter, settings.Transcription.Timing);
        Assert.Equal(VideoSettings.QualityBalanced, settings.Recording.Video.Quality);
        Assert.Null(settings.General.Validate());
    }

    [Fact]
    public async Task EmptyStringsWrittenByTwoPointOneAreRepairedOnLoad()
    {
        // 2.1.0 read the new fields as null and wrote them back as "", after which every settings.set was refused.
        using var directory = new Fakes.TempDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """
            { "schemaVersion": 2, "general": { "autoUpdate": false, "dateFormat": "", "libraryView": "" }, "recording": { "defaultType": "meeting" } }
            """);
        var store = new JsonSettingsStore(path, Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonSettingsStore>.Instance);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(Memento.Core.Formatting.DateFormats.Default, settings.General.DateFormat);
        Assert.Equal(GeneralSettings.TimelineView, settings.General.LibraryView);
        Assert.False(settings.General.AutoUpdate);
        Assert.Null(settings.General.Validate());
    }

    [Fact]
    public void EmptyBlocksReadExactlyAsNewInstances()
    {
        // Every block is a record, so this compares every property, including the ones with defaults.
        var settings = JsonSerializer.Deserialize("""{ "schemaVersion": 2, "general": {}, "recording": { "storage": {}, "video": {} }, "transcription": {}, "speakers": {}, "storage": {}, "history": {}, "export": {}, "ai": {}, "documents": {} }""", SettingsJsonContext.Default.AppSettings)!;

        Assert.Equal(new GeneralSettings(), settings.General);
        Assert.Equal(new RecordingSettings(), settings.Recording);
        Assert.Equal(new StorageSettings(), settings.Recording.Storage);
        Assert.Equal(new VideoSettings(), settings.Recording.Video);
        Assert.Equal(new TranscriptionSettings(), settings.Transcription);
        Assert.Equal(new SpeakerSettings(), settings.Speakers);
        Assert.Equal(new LibraryStorageSettings(), settings.Storage);
        Assert.Equal(new HistorySettings(), settings.History);
        // Export, AI and Documents hold lists, which records compare by reference; their scalar defaults are checked here.
        Assert.Equal(new ExportSettings().AskWhereEachTime, settings.Export.AskWhereEachTime);
        Assert.Equal(new ExportSettings().CreateSubfolder, settings.Export.CreateSubfolder);
        Assert.Equal(new DocumentsSettings().OcrEngine, settings.Documents.OcrEngine);
    }
}
