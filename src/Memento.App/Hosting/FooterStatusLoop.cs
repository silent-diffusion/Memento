using Memento.Core.Status;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Memento.App.Hosting;

/// <summary>Samples the footer status every 5 seconds (ARCHITECTURE.md §5.7) and pushes it when it changes.</summary>
internal sealed partial class FooterStatusLoop(FooterStatusService footer, ILogger<FooterStatusLoop> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly ILogger<FooterStatusLoop> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                footer.Publish(force: false);
            }
#pragma warning disable CA1031 // One failed sample must not stop the footer from updating.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogSampleFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Footer status sample failed")]
    private partial void LogSampleFailed(Exception exception);
}
