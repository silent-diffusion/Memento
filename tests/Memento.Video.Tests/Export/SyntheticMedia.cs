using System.Buffers.Binary;
using Memento.Video.Export;

namespace Memento.Video.Tests.Export;

/// <summary>Small synthetic media for the export tests: flat-coloured H.264 MP4s and a silent-ish PCM WAV. Nothing is played.</summary>
internal static class SyntheticMedia
{
    /// <summary>Writes <paramref name="frames"/> pictures filled with <paramref name="luma"/> (and neutral chroma), software-encoded.</summary>
    public static string Video(string path, int width, int height, int frameRate, int frames, Func<int, byte> luma)
    {
        var frame = new Nv12Frame(width, height);
        var period = TimeSpan.TicksPerSecond / frameRate;
        using (var writer = Mp4OutputWriter.Encoding(path, width, height, frameRate, 1_000_000, audio: null, preferHardware: false))
        {
            for (var i = 0; i < frames; i++)
            {
                frame.Data.AsSpan(0, width * height).Fill(luma(i));
                frame.Data.AsSpan(width * height).Fill(128);
                writer.WriteVideo(frame, i * period, period);
            }

            writer.Finish();
        }

        return path;
    }

    /// <summary>A 16-bit stereo 48 kHz WAV of a quiet square wave, <paramref name="seconds"/> long.</summary>
    public static string Wav(string path, double seconds)
    {
        const int rate = 48_000;
        const int channels = 2;
        var samples = (int)(seconds * rate);
        var data = new byte[samples * channels * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)((i / 100 % 2 == 0) ? 600 : -600);
            for (var c = 0; c < channels; c++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(((i * channels) + c) * 2), value);
            }
        }

        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + data.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(data.Length);
        w.Write(data);
        return path;
    }

    /// <summary>Decodes <paramref name="path"/>'s pictures: (time on the file's timeline, mean luma inside the rectangle).</summary>
    public static List<(long Time, double Luma)> Luma(string path, int width, int height, (int X, int Y, int W, int H) area)
    {
        var result = new List<(long, double)>();
        using var reader = new Nv12SegmentReader([new ComposeSegment(path, 0)]);
        var frame = new Nv12Frame(width, height);
        while (reader.TryRead(frame, out var time, out _))
        {
            double sum = 0;
            for (var y = area.Y; y < area.Y + area.H; y++)
            {
                for (var x = area.X; x < area.X + area.W; x++)
                {
                    sum += frame.Data[(y * width) + x];
                }
            }

            result.Add((time, sum / (area.W * area.H)));
        }

        return result;
    }
}
