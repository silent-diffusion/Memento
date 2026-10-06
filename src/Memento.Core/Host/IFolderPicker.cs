namespace Memento.Core.Host;

/// <summary>The Windows folder picker, shown over the main window.</summary>
public interface IFolderPicker
{
    /// <summary>Returns the chosen folder, or <c>null</c> when the user cancelled.</summary>
    Task<string?> PickAsync(string title, string? initialPath, CancellationToken cancellationToken);
}
