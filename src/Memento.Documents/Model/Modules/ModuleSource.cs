using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Modules;

/// <summary>Who produces a module's content.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ModuleSource>))]
public enum ModuleSource
{
    /// <summary>Written by the AI provider from the ticked inputs, then verified.</summary>
    Ai,

    /// <summary>Placed by the document composer from the project's data; no AI is involved.</summary>
    Data,

    /// <summary>Written by the user in the Builder or viewer.</summary>
    User,
}
