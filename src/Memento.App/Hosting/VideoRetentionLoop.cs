using Memento.Core.Audio;
using Memento.Core.Settings;
using Memento.Core.Video;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Memento.App.Hosting;

/// <summary>
/// Settings › Storage and history › Remove video older than (2.0, DESIGN.md §18.5): once the page is ready, then every
/// 24 hours and whenever the age is changed, the video of recordings older than it is removed
/// (<see cref="VideoRemovalService.SweepAsync"/>; audio, transcript and documents are kept, and each recording's History
/// says so). With the setting at Never (the default) nothing happens. 2.1: the same loop runs Delete audio tracks older
/// than (<see cref="AudioRemovalService.SweepAsync"/>; the transcript, documents and video are kept), after the video sweep.
/// </summary>
internal sealed partial class VideoRetentionLoop(VideoRemovalService removal, AudioRemovalService audio, ISettingsStore settings, UiLifecycle lifecycle, TimeProvider time, ILogger<VideoRetentionLoop> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly ILogger<VideoRetentionLoop> _logger = logger;
    private readonly SemaphoreSlim _changed = new(0, 1);

    public override void Dispose()
    {
        _changed.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Changed += OnSettingsChanged;
        try
        {
            await lifecycle.Ready.WaitAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await SweepAsync(stoppingToken);
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var changed = _changed.WaitAsync(wake.Token);
                await Task.WhenAny(changed, Task.Delay(Interval, time, wake.Token));
                await wake.CancelAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Memento is closing.
        }
        finally
        {
            settings.Changed -= OnSettingsChanged;
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var video = e.Current.Storage.RemoveVideoOlderThanDays is not null
            && e.Current.Storage.RemoveVideoOlderThanDays != e.Previous.Storage.RemoveVideoOlderThanDays;
        var audioAge = e.Current.Storage.DeleteAudioOlderThanDays is not null
            && e.Current.Storage.DeleteAudioOlderThanDays != e.Previous.Storage.DeleteAudioOlderThanDays;
        if ((video || audioAge) && _changed.CurrentCount == 0)
        {
            _changed.Release();
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            var removed = await removal.SweepAsync(cancellationToken);
            if (removed > 0)
            {
                LogSwept(removed);
            }

            var deleted = await audio.SweepAsync(cancellationToken);
            if (deleted > 0)
            {
                LogAudioSwept(deleted);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed sweep is tried again at the next one; nothing was removed by halves (the manifest is written first).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Remove video older than: {Count} recordings lost their video files")]
    private partial void LogSwept(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Delete audio older than: {Count} recordings lost their audio files")]
    private partial void LogAudioSwept(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remove video or delete audio older than: the sweep failed; it runs again later")]
    private partial void LogFailed(Exception exception);
}
