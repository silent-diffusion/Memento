namespace Memento.Core.Secrets;

/// <summary>The plaintext inside <c>secrets.bin</c> before DPAPI encryption. Exists in memory only.</summary>
internal sealed record SecretsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);
}
