namespace Memento.Core.Models;

/// <summary>
/// <c>&lt;file&gt;.verified.json</c> beside an installed model: the SHA-256 the file had when it was last hashed, and the
/// size and last-write time it had then. While both still match, the file counts as verified without hashing it again.
/// </summary>
internal sealed record ModelVerifiedStamp
{
    public const int CurrentSchemaVersion = 1;

    public const string Suffix = ".verified.json";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Lower-case hex SHA-256 of the file.</summary>
    public required string Sha256 { get; init; }

    public long SizeBytes { get; init; }

    public DateTime LastWriteUtc { get; init; }
}
