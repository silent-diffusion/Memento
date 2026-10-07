using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Records;

/// <summary>Wall-clock time per pipeline pass, in milliseconds.</summary>
public sealed record RecordTimings
{
    public long ComposeMs { get; init; }

    public long MapMs { get; init; }

    public long VerifyMs { get; init; }

    public long WriteMs { get; init; }

    /// <summary>Local only: loading the model (once per pass) and warming it up.</summary>
    public long ModelLoadMs { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
