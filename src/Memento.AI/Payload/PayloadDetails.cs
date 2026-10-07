namespace Memento.AI.Payload;

/// <summary>Recording details (PRODUCT-SPEC "Meeting Information").</summary>
/// <param name="Fields">Further labelled details in display order (for example "Project" → "Atlas").</param>
public sealed record PayloadDetails(
    string? Title = null,
    DateTimeOffset? RecordedAt = null,
    TimeSpan? Duration = null,
    string? Type = null,
    string? Location = null,
    string? Description = null,
    IReadOnlyList<KeyValuePair<string, string>>? Fields = null);
