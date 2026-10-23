using Memento.Core.Models;

namespace Memento.Core.Tests.Models;

public sealed class ModelCatalogTests
{
    private const string Entry = """
        { "id": "tiny", "engine": "whisper", "kind": "transcription", "role": null, "name": "Tiny", "description": "d",
          "fileName": "ggml-tiny.bin", "sizeBytes": 10, "sha256": "SHA", "url": "URL", "license": "MIT",
          "runsOn": "either", "minVramBytes": null, "recommendedFor": null, "accuracyNote": "n" }
        """;

    private static string Catalog(string sha = "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", string url = "https://huggingface.co/tiny", int schema = 1, int entries = 1) =>
        $$"""{ "schemaVersion": {{schema}}, "models": [ {{string.Join(",", Enumerable.Repeat(Entry.Replace("SHA", sha, StringComparison.Ordinal).Replace("URL", url, StringComparison.Ordinal), entries))}} ] }""";

    [Fact]
    public void TheBuiltInCatalogHasEveryM2ModelWithAVerifiedHash()
    {
        var catalog = ModelCatalog.Default;

        Assert.Equal(
            ["whisper-large-v3-turbo", "whisper-medium", "whisper-small", "whisper-base", "pyannote-segmentation-3-0", "nemo-titanet-small", "3dspeaker-eres2net-base", "tesseract-eng", "qwen3.5-4b-q4", "ministral-3-3b-q4", "qwen3.5-27b-q4", "qwen3.5-35b-a3b-q4"],
            catalog.Entries.Select(e => e.Id));
        Assert.Equal("1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", catalog.Find("whisper-large-v3-turbo")!.Sha256);
        Assert.Equal("6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208", catalog.Find("whisper-medium")!.Sha256);
        Assert.Equal("1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", catalog.Find("whisper-small")!.Sha256);
        Assert.Equal(1_624_555_275, catalog.Find("whisper-large-v3-turbo")!.SizeBytes);
        Assert.Equal("gpu", catalog.Find("whisper-large-v3-turbo")!.RecommendedFor);
        Assert.Equal("cpu", catalog.Find("whisper-small")!.RecommendedFor);
        Assert.Equal(ModelRoles.Segmentation, catalog.Find("pyannote-segmentation-3-0")!.Role);
        Assert.Equal(ModelRoles.Embedding, catalog.Find("nemo-titanet-small")!.Role);
        Assert.Equal(ModelKinds.Ocr, catalog.Find("tesseract-eng")!.Kind);
        Assert.All(catalog.Entries, e => Assert.StartsWith("https://", e.Url, StringComparison.Ordinal));
        Assert.DoesNotContain(catalog.Entries, e => e.Url.Contains("cuda", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParsesAValidCatalog()
    {
        var catalog = ModelCatalog.Parse(Catalog());

        var tiny = Assert.Single(catalog.Entries);
        Assert.Equal("tiny", tiny.Id);
        Assert.Same(tiny, catalog.Find("tiny"));
        Assert.Null(catalog.Find("missing"));
        Assert.Null(catalog.Find(null));
    }

    [Theory]
    [InlineData("ABC", "https://huggingface.co/tiny", 1, 1, "sha256")]
    [InlineData("1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", "http://huggingface.co/tiny", 1, 1, "https")]
    [InlineData("1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", "https://huggingface.co/tiny", 2, 1, "schema 2")]
    [InlineData("1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", "https://huggingface.co/tiny", 1, 2, "twice")]
    public void RejectsAnInvalidCatalogWithTheReason(string sha, string url, int schema, int entries, string reason)
    {
        var error = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(Catalog(sha, url, schema, entries)));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.org/tiny")]
    [InlineData("https://huggingface.co.example.org/tiny")]
    [InlineData("https://cdn-lfs.huggingface.co/tiny")]
    [InlineData("https://raw.githubusercontent.com/tiny")]
    public void RejectsADownloadAddressOffThePublishingHosts(string url)
    {
        var error = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(Catalog(url: url)));

        Assert.Contains("url must be on huggingface.co or github.com", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryBuiltInRepositoryFileIsPinnedToACommit()
    {
        // SA2-29: a branch can be re-uploaded upstream; a commit cannot. Release assets name no revision of the repository.
        foreach (var entry in ModelCatalog.Default.Entries)
        {
            var uri = new Uri(entry.Url);
            Assert.Null(ModelCatalog.MovingRevision(uri));
            var path = uri.AbsolutePath;
            Assert.True(
                System.Text.RegularExpressions.Regex.IsMatch(path, "^/[^/]+/[^/]+/(resolve|raw)/[0-9a-f]{40}/[^/]+$")
                    || (uri.Host == "github.com" && System.Text.RegularExpressions.Regex.IsMatch(path, "^/[^/]+/[^/]+/releases/download/[^/]+/[^/]+$")),
                $"{entry.Id} downloads from {entry.Url}, which is neither a file at a commit nor a release asset.");
        }
    }

    [Theory]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin", "main")]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/resolve/v1.0/ggml-small.bin", "v1.0")]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/raw/main/ggml-small.bin", "main")]
    [InlineData("https://huggingface.co/datasets/someone/data/resolve/main/x.bin", "main")]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b/ggml-small.bin", "5359861c739e955e79d9a303bcbc70fb988958b")]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861C739E955E79D9A303BCBC70FB988958B1/ggml-small.bin", "5359861C739E955E79D9A303BCBC70FB988958B1")]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/resolve/refs%2Fpr%2F1/ggml-small.bin", "refs/pr/1")]
    [InlineData("https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata", "main")]
    [InlineData("https://github.com/tesseract-ocr/tessdata_fast/blob/4.1.0/eng.traineddata", "4.1.0")]
    [InlineData("https://github.com/tesseract-ocr/tessdata_fast/raw/refs/heads/main/eng.traineddata", "refs")]
    public void RejectsAFileAtAMovingRevision(string url, string revision)
    {
        var error = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(Catalog(url: url)));

        Assert.Contains($"url names the moving revision '{revision}'", error.Message, StringComparison.Ordinal);
        Assert.Contains("40-character commit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small.bin")]
    [InlineData("https://huggingface.co/datasets/someone/data/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/x.bin")]
    [InlineData("https://github.com/tesseract-ocr/tessdata_fast/raw/87416418657359cb625c412a48b6e1d6d41c29bd/eng.traineddata")]
    [InlineData("https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/nemo_en_titanet_small.onnx")]
    [InlineData("http://127.0.0.1:5000/whisper/resolve/main/ggml-tiny.bin")]
    public void AcceptsAFileAtACommitOrAReleaseAsset(string url)
    {
        var catalog = ModelCatalog.Parse(Catalog(url: url));

        Assert.Equal(url, Assert.Single(catalog.Entries).Url);
    }

    [Theory]
    [InlineData("NUL")]
    [InlineData("nul.bin")]
    [InlineData("COM1.onnx")]
    [InlineData("lpt9")]
    [InlineData("ggml-tiny.bin.")]
    [InlineData("ggml-tiny.bin ")]
    [InlineData(" ggml-tiny.bin")]
    [InlineData("..")]
    [InlineData("a/b.bin")]
    [InlineData("a\\\\b.bin")]
    [InlineData("c:b.bin")]
    [InlineData("a*b.bin")]
    [InlineData("ggml-tiny.bin.part")]
    [InlineData("ggml-tiny.bin.verified.json")]
    [InlineData("ggml-tiny.bin.corrupt-20260101")]
    public void RejectsAFileNameWindowsOrTheModelManagerWouldMisread(string fileName)
    {
        var json = Catalog().Replace("\"ggml-tiny.bin\"", "\"" + fileName + "\"", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(json));

        Assert.Contains("fileName must be a plain Windows file name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLargerGroupIsOnlyForLocalLanguageModelsWithAPositiveMemoryFigure()
    {
        var catalog = ModelCatalog.Default;
        Assert.Equal(["qwen3.5-27b-q4", "qwen3.5-35b-a3b-q4"], catalog.Entries.Where(e => e.Group == ModelGroups.Larger).Select(e => e.Id));
        Assert.All(catalog.Entries.Where(e => e.Group == ModelGroups.Larger), e => Assert.True(e.MinRamBytes > e.SizeBytes));
        Assert.All(catalog.Entries.Where(e => e.Group is null), e => Assert.Null(e.MinRamBytes));

        var onWhisper = Catalog().Replace("\"accuracyNote\": \"n\"", "\"accuracyNote\": \"n\", \"group\": \"larger\"", StringComparison.Ordinal);
        Assert.Contains("group may only be 'larger', on a local language model", Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(onWhisper)).Message, StringComparison.Ordinal);
        var negative = Catalog().Replace("\"accuracyNote\": \"n\"", "\"accuracyNote\": \"n\", \"minRamBytes\": 0", StringComparison.Ordinal);
        Assert.Contains("minRamBytes must be positive", Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(negative)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTwoEntriesThatShareAFileWhateverTheCase()
    {
        var first = Entry.Replace("SHA", "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", StringComparison.Ordinal).Replace("URL", "https://huggingface.co/a", StringComparison.Ordinal);
        var second = first.Replace("\"tiny\"", "\"tiny-two\"", StringComparison.Ordinal).Replace("ggml-tiny.bin", "GGML-Tiny.bin", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse($$"""{ "schemaVersion": 1, "models": [ {{first}}, {{second}} ] }"""));

        Assert.Contains("'tiny-two'", error.Message, StringComparison.Ordinal);
        Assert.Contains("already uses the file name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoopbackDownloadAddressIsAllowedForTests()
    {
        var catalog = ModelCatalog.Parse(Catalog(url: "http://127.0.0.1:5000/tiny"));

        Assert.Equal("http://127.0.0.1:5000/tiny", catalog.Entries[0].Url);
    }

    [Fact]
    public void AMirrorOnThisPcKeepsEverySizeAndChecksum()
    {
        var mirrored = ModelCatalog.Default.WithMirror(new Uri("http://127.0.0.1:9470/"));

        Assert.Equal(ModelCatalog.Default.Entries.Count, mirrored.Entries.Count);
        var turbo = mirrored.Find("whisper-large-v3-turbo")!;
        Assert.Equal("http://127.0.0.1:9470/whisper/ggml-large-v3-turbo.bin", turbo.Url);
        Assert.Equal(ModelCatalog.Default.Find("whisper-large-v3-turbo")!.Sha256, turbo.Sha256);
        Assert.Equal(ModelCatalog.Default.Find("whisper-large-v3-turbo")!.SizeBytes, turbo.SizeBytes);
    }

    [Theory]
    [InlineData("https://example.org/models/")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("file:///C:/models/")]
    public void AMirrorElsewhereIsRefused(string mirror) =>
        Assert.Throws<ArgumentException>(() => ModelCatalog.Default.WithMirror(new Uri(mirror)));

    [Fact]
    public void RejectsMalformedJson()
    {
        Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse("{ not json"));
    }
}
