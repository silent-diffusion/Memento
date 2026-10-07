using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Attachments;

/// <summary>
/// One entry of the manifest's <c>attachments</c> index (<c>project.json</c>): what the bridge shows plus where the
/// file is and its SHA-256, computed when it was added.
/// </summary>
public sealed record AttachmentRecord
{
    public const string AgendaKind = "agenda";
    public const string FileKind = "file";

    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Relative to the project folder, forward slashes: <c>attachments/agenda.docx</c>.</summary>
    public required string File { get; init; }

    public long SizeBytes { get; init; }

    public string? Sha256 { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    /// <summary><see cref="AgendaKind"/> or <see cref="FileKind"/>.</summary>
    public string Kind { get; init; } = FileKind;

    public string? ContentType { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public Attachment ToContract() => new(Id, Name, SizeBytes, AddedAt, Kind, ContentType);
}
