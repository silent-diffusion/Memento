namespace Memento.Documents.Export;

/// <summary>
/// A document could not be exported. <see cref="Exception.Message"/> names the document and format, says the document
/// itself is unchanged and offers the fix; <see cref="Code"/> is a <see cref="DocumentExportErrorCodes"/> value.
/// </summary>
public sealed class DocumentExportException : Exception
{
    public DocumentExportException()
        : this(DocumentExportErrorCodes.WriteFailed, "The document could not be exported. The document itself is unchanged.")
    {
    }

    public DocumentExportException(string message)
        : this(DocumentExportErrorCodes.WriteFailed, message)
    {
    }

    public DocumentExportException(string message, Exception innerException)
        : this(DocumentExportErrorCodes.WriteFailed, message, innerException)
    {
    }

    public DocumentExportException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
