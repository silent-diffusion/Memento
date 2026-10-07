using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>templates.save</c>.</summary>
public sealed record TemplateSaveParams([property: JsonRequired] Template Template);
