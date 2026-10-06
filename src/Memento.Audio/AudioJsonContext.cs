using System.Text.Json.Serialization;
using Memento.Audio.Mixing;

namespace Memento.Audio;

/// <summary>Source-generated serialization for the files this assembly writes.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PeaksFile))]
internal sealed partial class AudioJsonContext : JsonSerializerContext
{
}
