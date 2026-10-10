using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Memento.Core.Voices;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Voices;

/// <summary><c>voices/known.json</c>: reading every version this Memento knows, damaged entries, newer files and writes.</summary>
public sealed class KnownVoicesStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly KnownVoicesStore _store;

    public KnownVoicesStoreTests()
    {
        _store = new KnownVoicesStore(new FakeLibraryLocation(_directory.File("Library")), new ManualTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero)), NullLogger<KnownVoicesStore>.Instance);
    }

    public void Dispose() => _directory.Dispose();

    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllText(_store.FilePath, json);
    }

    private const string Sample = """{ "recordingId": "r1", "speakerId": "spk1", "embedding": [1, 0, 0], "seconds": 30, "at": "2026-10-01T10:00:00+00:00" }""";

    [Fact]
    public async Task NoFileIsNoVoices()
    {
        var document = await _store.LoadAsync(CancellationToken.None);

        Assert.Empty(document.Voices);
        Assert.False(File.Exists(_store.FilePath));
    }

    [Fact]
    public async Task AWriteIsAtomicVersionedAndReadsBack()
    {
        await _store.UpdateAsync(d => (d with { Voices = [new KnownVoice { Id = "v1", Name = "Ana", EmbeddingModelId = "m", Samples = [new KnownVoiceSample("r1", "spk1", [1, 0, 0], 30, DateTimeOffset.UnixEpoch)] }] }, 0), CancellationToken.None);

        Assert.False(File.Exists(_store.FilePath + ".tmp"));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(_store.FilePath));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Ana", json.RootElement.GetProperty("voices")[0].GetProperty("name").GetString());
        Assert.Equal("Ana", Assert.Single((await _store.LoadAsync(CancellationToken.None)).Voices).Name);
    }

    [Fact]
    public async Task AFileWithoutAVersionIsReadAsVersionOne()
    {
        Write($$"""{ "voices": [ { "id": "v1", "name": "Ana", "embeddingModelId": "m", "samples": [{{Sample}}] } ] }""");

        var document = await _store.LoadAsync(CancellationToken.None);

        Assert.Equal(KnownVoicesDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal("Ana", Assert.Single(document.Voices).Name);
    }

    [Fact]
    public async Task DamagedEntriesAreDroppedAndTheOthersKept()
    {
        Write($$"""
            {
              "schemaVersion": 1,
              "voices": [
                { "id": "v1", "name": "Ana", "embeddingModelId": "m", "samples": [{{Sample}}, { "recordingId": "r2", "speakerId": "spk2", "embedding": [], "seconds": 3, "at": "2026-10-01T10:00:00+00:00" }] },
                { "id": "v2", "embeddingModelId": "m" },
                "not a voice",
                { "id": "v1", "name": "Duplicate", "embeddingModelId": "m" },
                { "id": "v3", "name": "  Ben  ", "embeddingModelId": "m", "samples": [{{Sample}}, { "recordingId": "r3", "speakerId": "spk1", "embedding": [1, 0], "seconds": 30, "at": "2026-10-01T10:00:00+00:00" }], "suggest": false, "futureField": 7 }
              ],
              "somethingNew": true
            }
            """);

        var document = await _store.LoadAsync(CancellationToken.None);

        Assert.Equal(["Ana", "Ben"], document.Voices.Select(v => v.Name));
        Assert.Single(document.Voices[0].Samples);
        Assert.Single(document.Voices[1].Samples); // the one of another length went
        Assert.False(document.Voices[1].Suggest);
        Assert.True(document.Voices[1].ExtensionData!.ContainsKey("futureField"));
        Assert.True(document.ExtensionData!.ContainsKey("somethingNew"));
    }

    [Fact]
    public async Task AFileFromANewerMementoIsRefusedAndLeftAsItIs()
    {
        const string newer = """{ "schemaVersion": 2, "voices": [] }""";
        Write(newer);

        await Assert.ThrowsAsync<KnownVoicesNewerException>(() => _store.LoadAsync(CancellationToken.None));
        await Assert.ThrowsAsync<KnownVoicesNewerException>(() => _store.UpdateAsync(d => (d, 0), CancellationToken.None));
        Assert.Equal(newer, await File.ReadAllTextAsync(_store.FilePath));
    }

    [Fact]
    public async Task AFileThatIsNotJsonIsSetAsideNeverDeleted()
    {
        Write("{ this is not json");

        var document = await _store.LoadAsync(CancellationToken.None);

        Assert.Empty(document.Voices);
        Assert.False(File.Exists(_store.FilePath));
        var aside = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_store.FilePath)!, "known.json.damaged-*"));
        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(aside));
    }

    [Fact]
    public async Task ForgetAllRemovesTheFileAndTheEmptyFolder()
    {
        await _store.UpdateAsync(d => (d with { Voices = [new KnownVoice { Id = "v1", Name = "Ana", EmbeddingModelId = "m" }] }, 0), CancellationToken.None);

        Assert.Equal(1, await _store.DeleteAsync(CancellationToken.None));

        Assert.False(Directory.Exists(Path.GetDirectoryName(_store.FilePath)));
    }
}
