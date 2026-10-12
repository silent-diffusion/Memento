using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Audio;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>audio.remove</c> (2.1): the Delete audio dialog's action (DESIGN.md §6.2, after the page's confirmation). Deletes the
/// recording's track files, mix and peaks; keeps the transcript, documents and video; writes History.
/// </summary>
public sealed class AudioRemoveMethod(AudioRemovalService removal) : BridgeMethod<RecordingIdParams, AudioRemoveResult>
{
    public override string Name => BridgeMethodNames.AudioRemove;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => LibraryBridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<AudioRemoveResult> ResultTypeInfo => LibraryBridgeJsonContext.Default.AudioRemoveResult;

    public override async Task<AudioRemoveResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        new(await removal.RemoveAsync(parameters.RecordingId, ProjectAudioRemoved.ByUser, cancellationToken));
}
