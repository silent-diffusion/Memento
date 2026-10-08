namespace Memento.Core.Host;

/// <summary>Windows did not let Memento write the clipboard (another program holds it open, or there is no window).</summary>
public sealed class ClipboardUnavailableException : Exception
{
    public ClipboardUnavailableException()
        : this("The clipboard is not available.")
    {
    }

    public ClipboardUnavailableException(string message)
        : base(message)
    {
    }

    public ClipboardUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
