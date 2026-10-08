using Memento.Core.Bridge;
using Memento.Core.Bridge.Methods;
using Memento.Core.Secrets;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Secrets;

/// <summary><c>ai.setKey</c> accepts only printable ASCII keys and never echoes a refused one.</summary>
public sealed class AiSetKeyValidationTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("sk-test-café-0123456789")]
    [InlineData("sk-test-“0123456789”")]
    [InlineData("sk-test-0123​456789")]
    [InlineData("sk-test-аbcdef0123456789")]
    [InlineData("sk-test-0123456789­abc")]
    public async Task AKeyWithANonAsciiCharacterIsRefusedAndNothingIsSaved(string key)
    {
        using var store = new DpapiSecretStore(new SecretStoreOptions(_directory.File("secrets.bin")), NullLogger<DpapiSecretStore>.Instance);

        var error = await Assert.ThrowsAsync<BridgeException>(() => new AiSetKeyMethod(store).InvokeAsync(new() { Provider = "openai", Key = key }, CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.Code);
        Assert.Contains("not a plain letter, digit or symbol", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(_directory.File("secrets.bin")));
    }

    [Fact]
    public async Task EveryPrintableAsciiSymbolIsAccepted()
    {
        using var store = new DpapiSecretStore(new SecretStoreOptions(_directory.File("secrets.bin")), NullLogger<DpapiSecretStore>.Instance);
        var key = "sk-" + new string(Enumerable.Range('!', '~' - '!' + 1).Select(c => (char)c).ToArray());

        var result = await new AiSetKeyMethod(store).InvokeAsync(new() { Provider = "openai", Key = key }, CancellationToken.None);

        Assert.True(result.HasKey);
        Assert.Equal(key, await store.GetKeyAsync("openai", CancellationToken.None));
    }
}
