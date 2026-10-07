using System.Windows;
using Memento.Core.Host;
using Microsoft.Win32;

namespace Memento.App.Hosting;

/// <summary><see cref="IFilePicker"/> with the WPF (Windows common item dialog) open-file picker, owned by the main window.</summary>
internal sealed class WpfFilePicker : IFilePicker
{
    public Task<string?> PickFileAsync(string title, IReadOnlyList<FileFilter> filters, CancellationToken cancellationToken)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("The file picker needs the Memento window.");
        return application.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Multiselect = false,
                CheckFileExists = true,
                Filter = FileFilter.ToFilterString(filters),
            };
            var owner = application.MainWindow;
            var chosen = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            return chosen == true ? dialog.FileName : null;
        }).Task;
    }
}
