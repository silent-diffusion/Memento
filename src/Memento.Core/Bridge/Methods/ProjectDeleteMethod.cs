using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Voices;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>project.delete</c>: the designed Delete flow. Refused with <c>project.recording</c> while that recording is active.
/// 2.0: the voice confirmations the recording gave known voices go with it (a voice left without any is forgotten).
/// </summary>
public sealed partial class ProjectDeleteMethod(ProjectService projects, KnownVoiceService voices, ILogger<ProjectDeleteMethod> logger) : BridgeMethod<RecordingIdParams, EmptyResult>
{
    private readonly ILogger<ProjectDeleteMethod> _logger = logger;

    public override string Name => BridgeMethodNames.ProjectDelete;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken)
    {
        await projects.DeleteAsync(parameters.RecordingId, cancellationToken);
        try
        {
            await voices.ForgetRecordingAsync(parameters.RecordingId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeException)
        {
            // The recording is gone; a known voices file that cannot be written now keeps its confirmations.
            LogVoicesKept(ex, parameters.RecordingId);
        }

        return new EmptyResult();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId} was deleted, but its known-voice confirmations could not be removed")]
    private partial void LogVoicesKept(Exception exception, string recordingId);
}
