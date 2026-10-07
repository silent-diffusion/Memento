using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>styles.get</c>, <c>styles.duplicate</c>, <c>styles.delete</c>, <c>styles.resetBuiltIn</c>.</summary>
public sealed record StyleIdParams([property: JsonRequired] string StyleId);
