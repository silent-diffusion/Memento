namespace Memento.Core.Projects;

/// <summary>SHA-256 of every track and the mix, computed at finalize. Keys are relative paths.</summary>
public sealed record ProjectIntegrity
{
    public string Algorithm { get; init; } = "sha256";

    public DateTimeOffset? ComputedAt { get; init; }

    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();
}
