namespace Memento.AI.Tests.Fakes;

/// <summary>An in-memory key reader with synthetic keys.</summary>
internal sealed class FakeSecrets : ISecretReader
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

    public FakeSecrets With(string provider, string key)
    {
        _keys[provider] = key;
        return this;
    }

    public int Reads { get; private set; }

    public bool HasKey(string provider) => _keys.ContainsKey(provider);

    public Task<string?> GetKeyAsync(string provider, CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(_keys.TryGetValue(provider, out var key) ? key : null);
    }
}
