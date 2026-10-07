using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;

namespace Memento.Transcription.Stages;

/// <summary>Appends History lines for a stage without letting a failed append stop the stage.</summary>
internal sealed partial class StageHistory(IProjectStore store, TimeProvider time, ILogger logger)
{
    private readonly ILogger _logger = logger;

    public async Task AppendAsync(string recordingId, string stage, string @event, string summary, string? detail)
    {
        try
        {
            await store.AppendHistoryAsync(recordingId, new HistoryEntry(time.GetLocalNow(), stage, @event, summary, detail), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException)
        {
            LogFailed(ex, recordingId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A history line for recording {RecordingId} could not be written")]
    private partial void LogFailed(Exception exception, string recordingId);
}
