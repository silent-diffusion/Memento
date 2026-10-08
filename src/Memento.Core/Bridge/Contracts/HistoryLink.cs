namespace Memento.Core.Bridge.Contracts;

/// <summary>A History line that changed the transcript or a document, and the stored copy it opens (<c>history.links</c>).</summary>
/// <param name="Index">The line's position in <c>Project.history</c> (oldest first, from 0).</param>
/// <param name="Kind"><c>transcript</c> or <c>document</c>.</param>
/// <param name="DocumentId">The document the line changed; <c>null</c> for the transcript, or when the document is not known.</param>
/// <param name="VersionId">
/// <c>current</c>, or a kept version's id (<c>transcript.getVersion</c>, <c>documents.getVersion</c>); <c>null</c> when no
/// copy of the content right after the line is kept.
/// </param>
/// <param name="After">The banner's words for that content: "after speakers were identified".</param>
public sealed record HistoryLink(int Index, string Kind, string? DocumentId, string? VersionId, string After);
