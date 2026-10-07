namespace Memento.Core.Host;

/// <summary>The Windows "open file" picker, shown over the main window.</summary>
public interface IFilePicker
{
    /// <summary>Returns the chosen file, or <c>null</c> when the user cancelled.</summary>
    Task<string?> PickFileAsync(string title, IReadOnlyList<FileFilter> filters, CancellationToken cancellationToken);
}
