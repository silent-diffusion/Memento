namespace Memento.Documents.Model;

/// <summary>
/// A document, template or style file could not be read. <see cref="Exception.Message"/> names the file and what is safe;
/// <see cref="Code"/> is a <see cref="DocumentFormatErrorCodes"/> value.
/// </summary>
public sealed class DocumentFormatException : Exception
{
    public DocumentFormatException()
        : this(DocumentFormatErrorCodes.Invalid, "The document file could not be read. Nothing was changed.")
    {
    }

    public DocumentFormatException(string message)
        : this(DocumentFormatErrorCodes.Invalid, message)
    {
    }

    public DocumentFormatException(string message, Exception innerException)
        : this(DocumentFormatErrorCodes.Invalid, message, innerException)
    {
    }

    public DocumentFormatException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
