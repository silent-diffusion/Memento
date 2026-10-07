using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>styles.sampleHtml</c>: the Style editor's settings as they are now (not saved).</summary>
public sealed record StyleSampleParams([property: JsonRequired] StyleSettings Settings);
