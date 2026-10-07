using Memento.Core.Bridge;
using Memento.Core.Bridge.Methods;
using Memento.Core.Secrets;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Secrets;

/// <summary>The key file is never rewritten from a read that failed, so one provider's save cannot wipe the other's key.</summary>
public sealed class DpapiSecretStoreTests : IDisposable
{
    private const string OpenAiKey = "sk-test-openai-0123456789";
    private const string AnthropicKey = "sk-test-anthropic-0123456789";

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private string FilePath => _directory.File("secrets.bin");

    private DpapiSecretStore Open() => new(new SecretStoreOptions(FilePath), NullLogger<DpapiSecretStore>.Instance);

    [Fact]
    public async Task ASaveWhileAnotherProgramHoldsTheFileFailsAndKeepsTheOtherKey()
    {
        using (var first = Open())
        {
            await first.SetKeyAsync("openai", OpenAiKey, CancellationToken.None);
        }

        using var store = Open();
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => store.SetKeyAsync("anthropic", AnthropicKey, CancellationToken.None));
        }

        using var reopened = Open();
        Assert.Equal(OpenAiKey, await reopened.GetKeyAsync("openai", CancellationToken.None));
        Assert.Null(await reopened.GetKeyAsync("anthropic", CancellationToken.None));
    }

    [Fact]
    public async Task ABusyFileIsReportedAsKeyWriteFailed()
    {
        using (var first = Open())
        {
            await first.SetKeyAsync("openai", OpenAiKey, CancellationToken.None);
        }

        using var store = Open();
        BridgeException error;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            error = await Assert.ThrowsAsync<BridgeException>(() => new AiSetKeyMethod(store).InvokeAsync(new() { Provider = "anthropic", Key = AnthropicKey }, CancellationToken.None));
        }

        Assert.Equal(DomainErrorCodes.AiKeyWriteFailed, error.Code);
        Assert.Contains("Nothing was saved and any other saved key is unchanged", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HasKeyLooksAgainAfterTheFileWasBusy()
    {
        using (var first = Open())
        {
            await first.SetKeyAsync("openai", OpenAiKey, CancellationToken.None);
        }

        using var store = Open();
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.HasKey("openai"));
        }

        Assert.True(store.HasKey("openai"));
    }

    [Fact]
    public async Task AnUnreadableFileIsKeptAsideBeforeANewKeyIsSaved()
    {
        byte[] damaged = [.. "MSEC"u8.ToArray(), DpapiSecretStore.FormatVersion, 1, 2, 3];
        await File.WriteAllBytesAsync(FilePath, damaged);
        using var store = Open();

        Assert.False(store.HasKey("openai"));
        await store.SetKeyAsync("openai", OpenAiKey, CancellationToken.None);

        Assert.True(store.HasKey("openai"));
        var aside = Assert.Single(Directory.GetFiles(_directory.Path, "secrets.bin.unreadable-*"));
        Assert.Equal(damaged, await File.ReadAllBytesAsync(aside));
        using var reopened = Open();
        Assert.Equal(OpenAiKey, await reopened.GetKeyAsync("openai", CancellationToken.None));
    }

    [Fact]
    public async Task AReadableFileIsUpdatedInPlaceWithNothingSetAside()
    {
        using var store = Open();
        await store.SetKeyAsync("openai", OpenAiKey, CancellationToken.None);
        await store.SetKeyAsync("anthropic", AnthropicKey, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(_directory.Path, "secrets.bin.unreadable-*"));
        Assert.Equal(OpenAiKey, await store.GetKeyAsync("openai", CancellationToken.None));
        Assert.Equal(AnthropicKey, await store.GetKeyAsync("anthropic", CancellationToken.None));
    }
}
