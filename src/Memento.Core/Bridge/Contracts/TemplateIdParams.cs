using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>templates.get</c>, <c>templates.duplicate</c>, <c>templates.delete</c>, <c>templates.resetBuiltIn</c>.</summary>
public sealed record TemplateIdParams([property: JsonRequired] string TemplateId);
