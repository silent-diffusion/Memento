namespace Memento.Documents.Agenda;

/// <summary>
/// An agenda could not be imported. <see cref="Exception.Message"/> is a user-facing sentence that names the file,
/// says that nothing was changed and offers the fix (DESIGN.md §17); <see cref="Code"/> is an <see cref="AgendaErrorCodes"/> value.
/// </summary>
public sealed class AgendaImportException : Exception
{
    public AgendaImportException()
        : this(AgendaErrorCodes.Unreadable, "The agenda could not be read. Nothing was changed.")
    {
    }

    public AgendaImportException(string message)
        : this(AgendaErrorCodes.Unreadable, message)
    {
    }

    public AgendaImportException(string message, Exception innerException)
        : this(AgendaErrorCodes.Unreadable, message, innerException)
    {
    }

    public AgendaImportException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
