namespace Memento.Documents.Export;

/// <summary>An exported document: the bytes, their SHA-256 (lower-case hex, for the export <c>manifest.json</c>) and how to name the file.</summary>
public sealed record DocumentExportResult(
    DocumentExportFormat Format,
    ReadOnlyMemory<byte> Content,
    string Sha256,
    string FileExtension,
    string MediaType)
{
    public long Length => Content.Length;
}
