using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// A document's content (BRIDGE.md M4). <see cref="Rows"/> is the stored block model as JSON
/// (<c>{ modules: [{ id, type, title, textSize, linkToTranscript, provenance, blocks }] }[]</c>, src/Memento.Documents/Model);
/// the UI shows documents through <c>documents.renderHtml</c>.
/// </summary>
/// <param name="Meta">The meta line under the title ("Meeting minutes · Sunday 5 October 2026, 4:00 PM · 1 h 10 min").</param>
public sealed record DocumentContent(int SchemaVersion, string Id, string Title, string Meta, JsonElement Rows, GenerationRecord? Record)
{
    public string? StyleId { get; init; }

    public int Version { get; init; }
}
