using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>library.list</c>. Every field is optional.</summary>
public sealed record LibraryListParams
{
    /// <summary>Searches titles and people (and, from M2, transcripts). Words match by prefix; all must match.</summary>
    public string? Query { get; init; }

    /// <summary>A recording type, or <c>all</c> (the default).</summary>
    public string? Type { get; init; }

    /// <summary><c>newest</c> (default), <c>oldest</c>, <c>longest</c>, <c>title</c> or (M3) <c>size</c>, largest first.</summary>
    public string? Sort { get; init; }

    /// <summary>
    /// 2.1: left out, every folder (the timeline); JSON <c>null</c>, only recordings at the root; a folder path, only the
    /// recordings filed directly in it. Search and the type filter apply inside it.
    /// </summary>
    public JsonElement Folder { get; init; }
}
