using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Audio;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>audio.removeEstimate</c> (2.1): what Delete audio would free: the named recordings, or every recording with audio
/// older than <c>olderThanDays</c> (null: every one). Feeds Settings' "Would free 3.1 GB now, from 12 recordings" and
/// the Delete audio dialog. Reads only.
/// </summary>
public sealed class AudioRemoveEstimateMethod(AudioRemovalService removal) : BridgeMethod<AudioRemoveEstimateParams, AudioRemoveEstimate>
{
    public override string Name => BridgeMethodNames.AudioRemoveEstimate;

    public override JsonTypeInfo<AudioRemoveEstimateParams> ParamsTypeInfo => LibraryBridgeJsonContext.Default.AudioRemoveEstimateParams;

    public override JsonTypeInfo<AudioRemoveEstimate> ResultTypeInfo => LibraryBridgeJsonContext.Default.AudioRemoveEstimate;

    public override async Task<AudioRemoveEstimate> InvokeAsync(AudioRemoveEstimateParams parameters, CancellationToken cancellationToken)
    {
        if (parameters.OlderThanDays is < LibraryStorageSettings.MinDays or > LibraryStorageSettings.MaxDays)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"olderThanDays is {LibraryStorageSettings.MinDays} to {LibraryStorageSettings.MaxDays}, or null.");
        }

        var (recordings, bytes) = await removal.EstimateAsync(parameters.RecordingIds, parameters.OlderThanDays, cancellationToken);
        return new AudioRemoveEstimate(recordings, bytes);
    }
}
