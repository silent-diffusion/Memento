namespace Memento.Core.Bridge.Contracts;

/// <summary>One document of a recording (<c>documents.list</c>).</summary>
/// <param name="Kind"><c>generated</c> or <c>written</c>.</param>
/// <param name="Version">Increments on every save.</param>
/// <param name="Versions">Earlier versions kept (when Settings › History keeps versions).</param>
public sealed record DocumentSummary(
    string Id,
    string Name,
    string Kind,
    string? TemplateName,
    string StyleId,
    string? ProviderId,
    DateTimeOffset? GeneratedAt,
    int Version,
    int Versions,
    DateTimeOffset ModifiedAt,
    long SizeBytes);
