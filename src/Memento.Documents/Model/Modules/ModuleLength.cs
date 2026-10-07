using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Modules;

/// <summary>The Builder's Length control (Short / Medium / Long).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ModuleLength>))]
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Short / Medium / Long are the Builder's own labels and the JSON values.")]
public enum ModuleLength
{
    Medium,
    Short,
    Long,
}
