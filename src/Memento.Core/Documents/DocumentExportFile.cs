namespace Memento.Core.Documents;

/// <summary>One exported document file.</summary>
/// <param name="Name">The file name ("Meeting minutes.docx"); made unique in the folder when it is written.</param>
/// <param name="EstimatedBytes">Exact for Word and Markdown; an estimate for PDF, which is printed when the file is written.</param>
/// <param name="WriteAsync">Writes the whole file at the path it is given.</param>
public sealed record DocumentExportFile(string Name, long EstimatedBytes, Func<string, CancellationToken, Task> WriteAsync, string? DocumentId = null);
