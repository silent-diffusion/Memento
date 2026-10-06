using System.Diagnostics;
using Memento.Audio.Writing;

namespace Memento.Audio.Tests;

/// <summary>
/// A child PowerShell process that plays a synthetic WAV through <c>Media.SoundPlayer</c>, so it owns an audio
/// session. Silent by default so tests do not make noise; a session exists whether or not the samples are zero.
/// </summary>
internal sealed class ChildPlayer : IDisposable
{
    private readonly Process _process;
    private readonly string _wav;

    private ChildPlayer(Process process, string wav)
    {
        _process = process;
        _wav = wav;
    }

    public int ProcessId => _process.Id;

    public static ChildPlayer Start(double seconds, double toneHz = 0, double amplitude = 0.1)
    {
        var wav = Path.Combine(Path.GetTempPath(), $"memento-test-tone-{Guid.NewGuid():N}.wav");
        var frames = (int)(seconds * 48_000);
        var bytes = new byte[frames * 4];
        if (toneHz > 0)
        {
            for (var i = 0; i < frames; i++)
            {
                var v = (short)(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * toneHz * i / 48_000));
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), v);
                BitConverter.TryWriteBytes(bytes.AsSpan((i * 4) + 2), v);
            }
        }

        Signals.WriteWav(wav, AudioFormat.Pcm16(48_000, 2), bytes);
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"(New-Object Media.SoundPlayer '{wav}').PlaySync()\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        return new ChildPlayer(Process.Start(psi)!, wav);
    }

    public void Dispose()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(2_000);
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
        try
        {
            File.Delete(_wav);
        }
        catch (IOException)
        {
        }
    }
}
