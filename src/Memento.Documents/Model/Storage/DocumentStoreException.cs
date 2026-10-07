namespace Memento.Documents.Model.Storage;

/// <summary>
/// A template or style store operation failed. <see cref="Exception.Message"/> names the item, says what is safe and
/// offers the fix; <see cref="Code"/> is a <see cref="StoreErrorCodes"/> value.
/// </summary>
public sealed class DocumentStoreException : Exception
{
    public DocumentStoreException()
        : this(StoreErrorCodes.Unreadable, "The item could not be read. Nothing was changed.")
    {
    }

    public DocumentStoreException(string message)
        : this(StoreErrorCodes.Unreadable, message)
    {
    }

    public DocumentStoreException(string message, Exception innerException)
        : this(StoreErrorCodes.Unreadable, message, innerException)
    {
    }

    public DocumentStoreException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
