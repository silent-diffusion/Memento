using System.Text.Json.Serialization;

namespace Memento.Core.Workers;

/// <summary>The worker protocol: one compact JSON object per line, camelCase, nulls left out.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(WorkerCommand))]
[JsonSerializable(typeof(WorkerReply))]
public sealed partial class WorkerJsonContext : JsonSerializerContext
{
}
