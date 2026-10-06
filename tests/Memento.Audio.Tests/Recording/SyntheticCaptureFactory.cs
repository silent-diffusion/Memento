using Memento.Audio.Capture;

namespace Memento.Audio.Tests.Recording;

/// <summary>Opens <see cref="SyntheticCapture"/>s; per-source loss times and failures are configurable.</summary>
internal sealed class SyntheticCaptureFactory : IAudioCaptureFactory
{
    public Dictionary<string, TimeSpan> LoseAfter { get; } = [];

    public HashSet<string> Unavailable { get; } = [];

    public List<SyntheticCapture> Opened { get; } = [];

    public Func<long, float> Signal { get; init; } = frame => (float)(0.5 * Math.Sin(2 * Math.PI * 440 * frame / 48_000));

    public async Task<IAudioCapture> OpenAsync(AudioSourceId source, CancellationToken cancellationToken)
    {
        await Task.Delay(5, cancellationToken);
        if (Unavailable.Contains(source.ToString()))
        {
            throw new AudioSourceUnavailableException(source, $"Synthetic source {source} is unplugged.");
        }

        var capture = new SyntheticCapture(source, Signal, LoseAfter.TryGetValue(source.ToString(), out var lose) ? lose : null);
        lock (Opened)
        {
            Opened.Add(capture);
        }

        return capture;
    }
}
