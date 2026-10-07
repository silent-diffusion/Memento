using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Memento.Generation.Bridge;

/// <summary>Source-generated serialization for the JSON nodes the bridge mapping builds.</summary>
[JsonSerializable(typeof(JsonArray))]
internal sealed partial class M4NodeJsonContext : JsonSerializerContext
{
}
