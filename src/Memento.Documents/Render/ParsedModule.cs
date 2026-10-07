using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;

namespace Memento.Documents.Render;

/// <summary>One module read back from paper markup.</summary>
public sealed record ParsedModule(string Id, string Type, string Title, TextSize TextSize, bool LinkToTranscript, IReadOnlyList<Block> Blocks);
