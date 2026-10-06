using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Audio.Capture;

/// <summary>Opens <see cref="WasapiAudioCapture"/>s.</summary>
public sealed class WasapiCaptureFactory(CaptureOptions? options = null, ILoggerFactory? loggerFactory = null) : IAudioCaptureFactory
{
    private readonly CaptureOptions _options = options ?? CaptureOptions.Default;
    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<WasapiAudioCapture>();

    public async Task<IAudioCapture> OpenAsync(AudioSourceId source, CancellationToken cancellationToken) =>
        await WasapiAudioCapture.OpenAsync(source, _options, _logger, cancellationToken).ConfigureAwait(false);
}
