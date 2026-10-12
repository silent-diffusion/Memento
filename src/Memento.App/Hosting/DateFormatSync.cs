using Memento.Core.Settings;
using Memento.Documents.Model;
using Microsoft.Extensions.Hosting;

namespace Memento.App.Hosting;

/// <summary>
/// 2.1, Settings › General › Date format: keeps the documents' meta line and running header (<see cref="MetaLine"/>)
/// on the chosen format, at start and whenever it changes. The page formats its own dates from the same setting.
/// </summary>
internal sealed class DateFormatSync(ISettingsStore settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        MetaLine.DateFormat = settings.Current.General.DateFormat;
        settings.Changed += OnChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        settings.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private static void OnChanged(object? sender, SettingsChangedEventArgs e) => MetaLine.DateFormat = e.Current.General.DateFormat;
}
