using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Maintenance;

/// <summary><c>project.changeType</c>: a built-in type or a custom one; History notes the change.</summary>
public sealed partial class ProjectTypeService(IProjectStore store, ProjectCatalog catalog, ProjectService projects, TimeProvider time, ILogger<ProjectTypeService> logger)
{
    private readonly ILogger<ProjectTypeService> _logger = logger;

    public async Task<Project> ChangeTypeAsync(string recordingId, string type, CancellationToken cancellationToken)
    {
        var value = (type ?? string.Empty).Trim();
        if (value.Length is 0 or > ProjectTypes.MaxLength || value.Any(char.IsControl))
        {
            throw M3Errors.Invalid($"A recording type needs a name of 1 to {ProjectTypes.MaxLength} characters, such as \"interview\" or a name of your own.");
        }

        if (!store.Exists(recordingId))
        {
            throw ProjectService.NotFound(recordingId);
        }

        var previous = (await store.LoadAsync(recordingId, cancellationToken)).Details.Type;
        if (!string.Equals(previous, value, StringComparison.Ordinal))
        {
            await catalog.UpdateAsync(recordingId, m => m with { Details = m.Details with { Type = value } }, cancellationToken);
            try
            {
                await store.AppendHistoryAsync(recordingId, new HistoryEntry(time.GetLocalNow(), "edited", "info", "Type changed", $"{previous} → {value}"), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogHistoryFailed(ex, recordingId);
            }
        }

        return await projects.GetAsync(recordingId, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A history line for recording {RecordingId} could not be written")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);
}
