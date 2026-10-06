using System.IO;
using System.Windows;
using Memento.Core.Host;
using Microsoft.Win32;

namespace Memento.App.Hosting;

/// <summary><see cref="IFolderPicker"/> with the WPF (Windows common item dialog) folder picker, owned by the main window.</summary>
internal sealed class WpfFolderPicker : IFolderPicker
{
    public Task<string?> PickAsync(string title, string? initialPath, CancellationToken cancellationToken)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("The folder picker needs the Memento window.");
        return application.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            if (initialPath is not null && Directory.Exists(initialPath))
            {
                dialog.InitialDirectory = initialPath;
            }

            var owner = application.MainWindow;
            var chosen = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            return chosen == true ? dialog.FolderName : null;
        }).Task;
    }
}
