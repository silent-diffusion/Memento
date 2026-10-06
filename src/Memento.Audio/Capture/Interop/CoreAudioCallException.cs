namespace Memento.Audio.Capture.Interop;

/// <summary>A Core Audio call failed; <see cref="Exception.HResult"/> carries its HRESULT and the message names the call.</summary>
internal sealed class CoreAudioCallException : Exception
{
    public CoreAudioCallException()
    {
    }

    public CoreAudioCallException(string message)
        : base(message)
    {
    }

    public CoreAudioCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CoreAudioCallException(string call, int hResult)
        : base($"{call} failed with 0x{hResult:X8}.")
    {
        HResult = hResult;
    }
}
