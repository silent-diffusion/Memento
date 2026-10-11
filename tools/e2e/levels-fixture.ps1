<#
.SYNOPSIS
  Writes the two-track level fixture (ENGINE-NOTES.md §Q): a loud "system audio" track and a quiet "microphone" track
  that say different things at different times, each a 48 kHz 16-bit WAV with its truth (<wav>.json, every line's
  text and where its voice starts and ends, from tools/e2e/sync-fixture.ps1). The system track is the first script
  (stereo) scaled to a -12 dBFS peak; the microphone track is the second script (mono) scaled to a -38 dBFS peak with
  steady room noise (pink, -60 dBFS RMS) under it, as a quiet microphone far from the speaker sounds. Synthetic
  voices only; nothing is downloaded or played.

.PARAMETER Folder
  Where to write system.wav, mic.wav and their .json files (keep it under artifacts/, which git ignores).

.EXAMPLE
  powershell -File tools/e2e/levels-fixture.ps1 -Folder artifacts/e2e-fixtures/levels
#>
param(
  [Parameter(Mandatory = $true)] [string] $Folder,
  [double] $SystemPeakDb = -12,
  [double] $MicPeakDb = -38,
  [double] $NoiseDb = -60,
  [double] $Minutes = 4
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class LevelsFixture
{
    // Reads a 16-bit PCM WAV written by sync-fixture.ps1 (44-byte header).
    public static short[] Read(string path, out int rate, out int channels)
    {
        var bytes = File.ReadAllBytes(path);
        channels = BitConverter.ToInt16(bytes, 22);
        rate = BitConverter.ToInt32(bytes, 24);
        var samples = new short[(bytes.Length - 44) / 2];
        Buffer.BlockCopy(bytes, 44, samples, 0, samples.Length * 2);
        return samples;
    }

    // Scales the samples so their peak is peakDb, adds pink noise at noiseDb RMS (none when noiseDb <= -120), writes 16-bit PCM.
    public static void Write(string path, short[] samples, int rate, int channels, double peakDb, double noiseDb, int seed)
    {
        int peak = 1;
        foreach (var s in samples) { peak = Math.Max(peak, Math.Abs((int)s)); }
        double gain = Math.Pow(10, peakDb / 20) * 32767.0 / peak;
        int frames = samples.Length / channels;
        var noise = new double[frames];
        if (noiseDb > -120)
        {
            // Pink-ish noise: white noise through Paul Kellet's economy filter, then scaled to the RMS asked for.
            var random = new Random(seed);
            double b0 = 0, b1 = 0, b2 = 0, sum = 0;
            for (int f = 0; f < frames; f++)
            {
                double white = (random.NextDouble() * 2) - 1;
                b0 = (0.99765 * b0) + (white * 0.0990460);
                b1 = (0.96300 * b1) + (white * 0.2965164);
                b2 = (0.57000 * b2) + (white * 1.0526913);
                noise[f] = b0 + b1 + b2 + (white * 0.1848);
                sum += noise[f] * noise[f];
            }
            double scale = Math.Pow(10, noiseDb / 20) * 32767.0 / Math.Sqrt(sum / frames);
            for (int f = 0; f < frames; f++) { noise[f] *= scale; }
        }

        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            double v = (samples[i] * gain) + noise[i / channels];
            short s = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(v)));
            data[2 * i] = (byte)(s & 0xFF);
            data[(2 * i) + 1] = (byte)((s >> 8) & 0xFF);
        }

        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); writer.Write(36 + data.Length);
            writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' }); writer.Write(16);
            writer.Write((short)1); writer.Write((short)channels); writer.Write(rate);
            writer.Write(rate * 2 * channels); writer.Write((short)(2 * channels)); writer.Write((short)16);
            writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); writer.Write(data.Length); writer.Write(data);
        }
    }
}
'@

$full = [System.IO.Path]::GetFullPath($Folder)
New-Item -ItemType Directory -Force -Path $full | Out-Null
$sync = Join-Path $PSScriptRoot 'sync-fixture.ps1'
$rawSystem = Join-Path $full 'raw-system.wav'
$rawMic = Join-Path $full 'raw-mic.wav'
& $sync -Path $rawSystem -SampleRate 48000 -Channels 2 -Minutes $Minutes -Script first | Out-Null
& $sync -Path $rawMic -SampleRate 48000 -Channels 1 -Minutes $Minutes -Script second | Out-Null

foreach ($track in @(@{ Raw = $rawSystem; Name = 'system.wav'; Peak = $SystemPeakDb; Noise = -200; Seed = 1 }, @{ Raw = $rawMic; Name = 'mic.wav'; Peak = $MicPeakDb; Noise = $NoiseDb; Seed = 2 })) {
  $rate = 0; $channels = 0
  $samples = [LevelsFixture]::Read($track.Raw, [ref] $rate, [ref] $channels)
  $target = Join-Path $full $track.Name
  [LevelsFixture]::Write($target, $samples, $rate, $channels, $track.Peak, $track.Noise, $track.Seed)
  Move-Item -Force ($track.Raw + '.json') ($target + '.json')
  Remove-Item -Force $track.Raw
}

Write-Output ("Wrote {0}: system.wav (peak {1} dBFS) and mic.wav (peak {2} dBFS, room noise {3} dBFS RMS), each with its truth .json." -f $Folder, $SystemPeakDb, $MicPeakDb, $NoiseDb)
