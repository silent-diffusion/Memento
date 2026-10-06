using System.Collections.Concurrent;
using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Recording.Simulation;

/// <summary>
/// Three fake sources for tests, the soak harness and <c>--simulate-audio</c>: a mono microphone, stereo system
/// audio and a stereo "meeting app", all 48 kHz 16-bit. A source can be switched off to simulate an unplugged device.
/// </summary>
public sealed class SimulatedAudioSourceProvider : IAudioSourceProvider
{
    public static readonly AudioSource Microphone = new("mic:simulated-1", "microphone", "Simulated microphone", "Simulated · 48 kHz mono", IsDefault: true, ProcessId: null);

    public static readonly AudioSource SystemAudio = new("system:simulated", "system", "Simulated system audio", "Everything this PC plays (simulated)", IsDefault: true, ProcessId: null);

    public static readonly AudioSource MeetingApp = new("app:4242", "application", "Simulated meeting app", "Only this app (simulated)", IsDefault: false, ProcessId: 4242);

    public const int SampleRate = 48_000;

    private readonly ConcurrentDictionary<string, bool> _unavailable = new(StringComparer.Ordinal);

    public static IReadOnlyList<AudioSource> All { get; } = [Microphone, SystemAudio, MeetingApp];

    public static PcmFormat FormatOf(AudioSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return PcmFormat.Pcm16(SampleRate, source.Kind == "microphone" ? 1 : 2);
    }

    /// <summary>Switches a source off (unplugged) or back on.</summary>
    public void SetAvailable(string sourceId, bool available)
    {
        if (available)
        {
            _unavailable.TryRemove(sourceId, out _);
        }
        else
        {
            _unavailable[sourceId] = true;
        }
    }

    public bool IsAvailable(string sourceId) =>
        All.Any(s => s.Id == sourceId) && !_unavailable.ContainsKey(sourceId);

    public Task<IReadOnlyList<AudioSource>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AudioSource>>(All.Where(s => IsAvailable(s.Id)).ToList());
}
