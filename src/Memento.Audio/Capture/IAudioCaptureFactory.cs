namespace Memento.Audio.Capture;

/// <summary>Opens capture streams; the WASAPI implementation is <see cref="WasapiCaptureFactory"/>, tests substitute synthetic sources.</summary>
public interface IAudioCaptureFactory
{
    /// <summary>Opens and initialises <paramref name="source"/> without starting it.</summary>
    /// <exception cref="AudioSourceUnavailableException">The device, endpoint or process cannot be captured.</exception>
    Task<IAudioCapture> OpenAsync(AudioSourceId source, CancellationToken cancellationToken);
}
