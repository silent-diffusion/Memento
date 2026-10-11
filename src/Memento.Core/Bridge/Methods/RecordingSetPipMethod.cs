using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>recording.setPip</c> (2.0): the camera inset on or off and its corner, from now on. Screen and camera are always
/// recorded as two tracks; the live preview and Review draw the inset, and the export's "One file" burns it in where it was.
/// </summary>
public sealed class RecordingSetPipMethod(RecordingCoordinator recordings) : BridgeMethod<RecordingSetPipParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.RecordingSetPip;

    public override JsonTypeInfo<RecordingSetPipParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.RecordingSetPipParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => VideoBridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(RecordingSetPipParams parameters, CancellationToken cancellationToken)
    {
        await recordings.SetPipAsync(parameters.SessionId, parameters.Enabled, parameters.Corner, cancellationToken);
        return new EmptyResult();
    }
}
