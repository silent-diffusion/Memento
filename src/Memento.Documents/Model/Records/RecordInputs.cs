using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Records;

/// <summary>The inputs that were part of the payload (the ticked ones that Settings allowed). Audio and video have no field.</summary>
public sealed record RecordInputs
{
    public bool Transcript { get; init; }

    public bool Details { get; init; }

    public bool Participants { get; init; }

    public bool Agenda { get; init; }

    public bool Highlights { get; init; }

    public bool Attachments { get; init; }

    public bool PreviousDocuments { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
