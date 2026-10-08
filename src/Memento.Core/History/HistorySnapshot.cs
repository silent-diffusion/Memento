using System.Globalization;

namespace Memento.Core.History;

/// <summary>
/// One stored copy of the transcript or of a document: the current content (<see cref="CurrentId"/>) or a kept version.
/// It was the content from <see cref="Start"/> (the write that defined it, its <c>lastChange.at</c>) until
/// <see cref="End"/> (when a write replaced it; <c>null</c> for the current content).
/// </summary>
/// <param name="Kind"><see cref="HistoryKinds.Transcript"/> or <see cref="HistoryKinds.Document"/>.</param>
/// <param name="DocumentId">The document, for a document copy.</param>
/// <param name="VersionId"><see cref="CurrentId"/>, or the id <c>transcript.versions</c> / <c>documents.versions</c> list.</param>
/// <param name="Start">When the content came about; <c>null</c> when unknown (an older file).</param>
/// <param name="End">When it was replaced; <c>null</c> for the current content.</param>
public sealed record HistorySnapshot(string Kind, string? DocumentId, string VersionId, DateTimeOffset? Start, DateTimeOffset? End)
{
    public const string CurrentId = "current";

    private const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    /// <summary>The time a kept version's id stands for (the UTC moment it was replaced), or <c>null</c>.</summary>
    public static DateTimeOffset? StampOf(string versionId) =>
        DateTime.TryParseExact(versionId, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
            : null;
}
