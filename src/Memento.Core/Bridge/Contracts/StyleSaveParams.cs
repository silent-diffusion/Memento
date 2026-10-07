using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>styles.save</c>.</summary>
public sealed record StyleSaveParams([property: JsonRequired] Style Style);
