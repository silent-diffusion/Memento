using System.Diagnostics;
using Memento.Audio;
using Memento.Audio.Writing;

namespace Memento.Tools.AudioCheck;

/// <summary>A child PowerShell process playing a synthetic tone, so there is one app to capture with process loopback.</summary>
internal sealed class TonePlayer : IDisposable
{
    private readonly Process _process;

    private TonePlayer(Process process, string wav)
    {
        _process = process;
        WavPath = wav;
    }

    public int ProcessId => _process.Id;

    public string WavPath { get; }

    /// <summary>Writes a 440 Hz, −20 dBFS stereo tone of <paramref name="seconds"/> and starts playing it.</summary>
    public static TonePlayer Start(string directory, int seconds)
    {
        var wav = Path.Combine(directory, "tone440.wav");
        if (!File.Exists(wav))
        {
            var frames = seconds * 48_000;
            var bytes = new byte[frames * 4];
            for (var i = 0; i < frames; i++)
            {
                var v = (short)(0.1 * short.MaxValue * Math.Sin(2 * Math.PI * 440 * i / 48_000));
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), v);
                BitConverter.TryWriteBytes(bytes.AsSpan((i * 4) + 2), v);
            }

            using var writer = new StreamingWavWriter(wav, AudioFormat.Pcm16(48_000, 2), durableCheckpoints: false);
            writer.Write(bytes);
        }

        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"(New-Object Media.SoundPlayer '{wav}').PlaySync()\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        return new TonePlayer(Process.Start(psi) ?? throw new InvalidOperationException("Could not start powershell.exe."), wav);
    }

    public void Dispose()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
    }
}
