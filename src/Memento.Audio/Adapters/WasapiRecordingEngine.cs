using Memento.Audio.Capture;
using Memento.Audio.Recording;
using Memento.Core.Recording;
using Microsoft.Extensions.Logging;

namespace Memento.Audio.Adapters;

/// <summary>
/// <see cref="IRecordingEngine"/> over <see cref="AudioRecordingSession"/>: WASAPI capture for microphones, endpoint
/// loopback for system audio and process loopback for applications, one int24 WAV per source under the project's
/// <c>tracks/</c> folder, checkpoints on the plan's interval. Either every source opens or none starts.
/// </summary>
public sealed partial class WasapiRecordingEngine(WasapiEngineOptions options, TimeProvider time, ILoggerFactory loggerFactory) : IRecordingEngine
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<WasapiRecordingEngine>();

    public string Name => "WASAPI";

    public async Task<IRecordingSession> StartAsync(RecordingPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.PreferredFormat is not null)
        {
            LogFormatIgnored(_logger);
        }

        foreach (var source in plan.Sources)
        {
            if (!AudioSourceId.TryParse(source.Id, out _))
            {
                throw new SourceUnavailableException(source.Id, source.Name, "it is not a source this PC offers");
            }
        }

        var audioOptions = new AudioRecordingOptions(Path.Combine(plan.ProjectFolder, Core.Projects.ProjectLayout.TracksFolder), plan.Sources.Select(s => s.Id).ToList())
        {
            CheckpointInterval = plan.CheckpointInterval,
            CaptureFactory = options.CaptureFactory,
            Describe = options.Describe,
            RolloverBytes = options.RolloverBytes,
            DurableCheckpoints = options.DurableCheckpoints,
            LoggerFactory = loggerFactory,
        };

        AudioRecordingSession session;
        try
        {
            session = await AudioRecordingSession.StartAsync(audioOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (AudioSourceUnavailableException ex)
        {
            var id = ex.SourceId.ToString();
            var source = plan.Sources.FirstOrDefault(s => s.Id == id);
            LogSourceUnavailable(_logger, ex, id);
            throw new SourceUnavailableException(id, source?.Name ?? "A source", SourceUnavailableReason.From(ex));
        }

        return new WasapiRecordingSession(session, plan, time, loggerFactory.CreateLogger<WasapiRecordingSession>());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A preferred capture format was requested; WASAPI keeps each device's own format and resamples only at mixdown")]
    private static partial void LogFormatIgnored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {SourceId} could not be opened; nothing was started")]
    private static partial void LogSourceUnavailable(ILogger logger, Exception exception, string sourceId);
}
