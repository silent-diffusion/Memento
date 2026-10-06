using System.Runtime.InteropServices;

namespace Memento.Audio.Capture.Interop;

/// <summary>
/// Completion handler for <c>ActivateAudioInterfaceAsync</c>. Agile (implements <c>IAgileObject</c>) because
/// Windows calls it on an MTA worker thread; without that marker activation fails from STA hosts.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class ActivationCompletionHandler : CoreAudio.IActivateAudioInterfaceCompletionHandler, CoreAudio.IAgileObject
{
    private readonly TaskCompletionSource<CoreAudio.IAudioClient> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<CoreAudio.IAudioClient> Completion => _completion.Task;

    public int ActivateCompleted(CoreAudio.IActivateAudioInterfaceAsyncOperation activateOperation)
    {
        try
        {
            var hr = activateOperation.GetActivateResult(out var activateResult, out var activated);
            if (hr < 0)
            {
                _completion.TrySetException(new CoreAudioCallException("IActivateAudioInterfaceAsyncOperation::GetActivateResult", hr));
            }
            else if (activateResult < 0)
            {
                _completion.TrySetException(new CoreAudioCallException("ActivateAudioInterfaceAsync", activateResult));
            }
            else
            {
                _completion.TrySetResult((CoreAudio.IAudioClient)activated);
            }
        }
#pragma warning disable CA1031 // Any failure must reach the waiting thread instead of crashing the MTA callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _completion.TrySetException(ex);
        }

        return CoreAudio.SOk;
    }
}
