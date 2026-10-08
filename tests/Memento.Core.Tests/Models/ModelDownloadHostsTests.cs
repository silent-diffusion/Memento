using Memento.Core.Models;

namespace Memento.Core.Tests.Models;

public sealed class ModelDownloadHostsTests
{
    [Theory]
    [InlineData("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin", "https://cdn-lfs.huggingface.co/repos/x/y")]
    [InlineData("https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/main/q.gguf", "https://cas-bridge.xethub.hf.co/xet-bridge/z")]
    [InlineData("https://github.com/k2-fsa/sherpa-onnx/releases/download/a/b.onnx", "https://objects.githubusercontent.com/github-production-release-asset/b")]
    [InlineData("https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata", "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata")]
    [InlineData("https://huggingface.co/a", "https://huggingface.co/b")]
    [InlineData("http://127.0.0.1:5000/a", "http://127.0.0.1:5000/b")]
    public void TheModelHostsAndTheirFileServersAreAllowed(string requested, string answered)
    {
        Assert.True(ModelDownloadHosts.IsAllowedDownload(new Uri(requested), new Uri(answered)));
    }

    [Theory]
    [InlineData("https://huggingface.co/a", "http://cdn-lfs.huggingface.co/a")]
    [InlineData("https://huggingface.co/a", "https://example.org/a")]
    [InlineData("https://huggingface.co/a", "https://huggingface.co.example.org/a")]
    [InlineData("https://huggingface.co/a", "https://evilhuggingface.co/a")]
    [InlineData("https://huggingface.co/a", "https://hf.co.example.org/a")]
    [InlineData("https://github.com/a", "https://githubusercontent.com.example.org/a")]
    [InlineData("https://huggingface.co/a", "http://127.0.0.1:5000/a")]
    [InlineData("http://127.0.0.1:5000/a", "http://localhost:5000/a")]
    [InlineData("http://127.0.0.1:5000/a", "http://127.0.0.1:5001/a")]
    [InlineData("http://127.0.0.1:5000/a", "https://huggingface.co/a")]
    public void AnyOtherDestinationIsRefused(string requested, string answered)
    {
        Assert.False(ModelDownloadHosts.IsAllowedDownload(new Uri(requested), new Uri(answered)));
    }

    [Fact]
    public void NoAnswerAddressIsRefused()
    {
        Assert.False(ModelDownloadHosts.IsAllowedDownload(new Uri("https://huggingface.co/a"), null));
    }

    [Theory]
    [InlineData("https://huggingface.co/a", true)]
    [InlineData("https://GitHub.com/a", true)]
    [InlineData("http://huggingface.co/a", false)]
    [InlineData("https://user@huggingface.co/a", false)]
    [InlineData("https://objects.githubusercontent.com/a", false)]
    [InlineData("https://example.org/a", false)]
    [InlineData("http://127.0.0.1:5000/a", true)]
    public void ACatalogAddressMustBeOnAPublishingHost(string url, bool allowed)
    {
        Assert.Equal(allowed, ModelDownloadHosts.IsAllowedCatalogUrl(new Uri(url)));
    }
}
