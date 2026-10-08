using Memento.Core.Updates;
using Microsoft.Extensions.Hosting;

namespace Memento.App.Hosting;

/// <summary>Runs <see cref="UpdateService"/>'s schedule: the first check once the interface reports ready.</summary>
internal sealed class UpdateLoop(UpdateService updates, UiLifecycle lifecycle) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => updates.RunAsync(lifecycle.Ready, stoppingToken);
}
