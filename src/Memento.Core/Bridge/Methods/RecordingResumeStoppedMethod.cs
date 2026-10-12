using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Maintenance;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>recording.resumeStopped</c> (2.1, "Resume recording"): a new segment in a stopped recording started today. The
/// result is a new session in the same recording, driven like <c>recording.start</c>'s (pause, stop, highlights).
/// Refused with <c>recording.cannotResume</c> (the reason in words), <c>recording.alreadyActive</c>,
/// <c>recording.sourceUnavailable</c>, <c>project.notFound</c> or <c>library.busy</c> while the library is being moved.
/// </summary>
public sealed class RecordingResumeStoppedMethod(RecordingCoordinator recordings, LibraryActivity? activity = null) : BridgeMethod<RecordingResumeStoppedParams, RecordingStartResult>
{
    public override string Name => BridgeMethodNames.RecordingResumeStopped;

    public override JsonTypeInfo<RecordingResumeStoppedParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingResumeStoppedParams;

    public override JsonTypeInfo<RecordingStartResult> ResultTypeInfo => BridgeJsonContext.Default.RecordingStartResult;

    public override async Task<RecordingStartResult> InvokeAsync(RecordingResumeStoppedParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.RecordingId))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, "Name the recording to resume (recordingId).");
        }

        using var starting = activity?.BeginRecordingStart();
        return await recordings.ResumeStoppedAsync(parameters.RecordingId, parameters.SourceIds, cancellationToken);
    }
}
