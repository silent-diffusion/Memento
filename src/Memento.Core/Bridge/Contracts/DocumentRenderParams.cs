namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.renderHtml</c>; <paramref name="Mode"/> is <c>view</c> or <c>print</c>.</summary>
public sealed record DocumentRenderParams(string RecordingId, string DocumentId, string Mode);
