using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>
/// The write that produced a document's current content (<c>generated</c>, <c>regenerated</c>, <c>edited</c>,
/// <c>restored</c>, <c>created</c>, <c>renamed</c>). It becomes the reason of the version the content turns into, and
/// decides whether an edit keeps a version (the first edit after a generation or a restore does; a run of edits is one).
/// </summary>
public sealed record DocumentChange
{
    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset At { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
