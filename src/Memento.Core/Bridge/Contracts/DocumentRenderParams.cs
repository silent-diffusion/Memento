using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.renderHtml</c>; <paramref name="Mode"/> is <c>view</c> or <c>print</c>.</summary>
public sealed record DocumentRenderParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string DocumentId, [property: JsonRequired] string Mode);
