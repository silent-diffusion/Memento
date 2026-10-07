namespace Memento.Core.Export;

/// <summary>One file an export writes.</summary>
/// <param name="Component">The dialog row (<see cref="ExportComponents"/>).</param>
/// <param name="Name">The file name (made unique in the folder when it is written).</param>
/// <param name="Folder">A folder below the output folder (attachments), or <c>null</c>.</param>
/// <param name="EstimatedBytes">Exact for copies and text; an estimate for converted audio.</param>
/// <param name="WriteAsync">Writes the whole file at the path it is given.</param>
public sealed record ExportItem(string Component, string Name, string? Folder, long EstimatedBytes, Func<string, CancellationToken, Task> WriteAsync);
