using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Transcripts;

/// <summary>Source-generated serialization for <c>transcript.json</c>, its versions and the partial file.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(TranscriptDocument))]
[JsonSerializable(typeof(TranscriptVersionFile))]
[JsonSerializable(typeof(TranscriptPartial))]
internal sealed partial class TranscriptJsonContext : JsonSerializerContext
{
}
