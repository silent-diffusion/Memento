using System.Text.Json.Serialization;

namespace Memento.Core.Attachments;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<AttachmentRecord>))]
internal sealed partial class AttachmentsJsonContext : JsonSerializerContext
{
}
