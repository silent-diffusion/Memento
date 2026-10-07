using System.Text;

namespace Memento.Audio.Tests.M3;

/// <summary>
/// A tiny synthetic AVI: one uncompressed 16 × 16 video frame per second and 16-bit PCM mono audio, so media import
/// can be tested with a video container without any video encoder.
/// </summary>
internal static class AviWriter
{
    private const int Width = 16;
    private const int Height = 16;
    private const int FrameBytes = Width * Height * 3;

    public static void Write(string path, int sampleRate, short[] samples)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(samples.Length / (double)sampleRate));
        var movi = new MemoryStream();
        var index = new List<(string Id, int Offset, int Length)>();
        using (var w = new BinaryWriter(movi, Encoding.ASCII, leaveOpen: true))
        {
            w.Write("movi"u8);
            for (var s = 0; s < seconds; s++)
            {
                index.Add(("00db", (int)movi.Position, FrameBytes));
                Chunk(w, "00db", new byte[FrameBytes].Select((_, i) => (byte)((i * 7) + (s * 40))).ToArray());
                var count = Math.Min(sampleRate, samples.Length - (s * sampleRate));
                var audio = new byte[Math.Max(0, count) * 2];
                Buffer.BlockCopy(samples, s * sampleRate * 2, audio, 0, audio.Length);
                index.Add(("01wb", (int)movi.Position, audio.Length));
                Chunk(w, "01wb", audio);
            }
        }

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file, Encoding.ASCII);
        var hdrl = Hdrl(sampleRate, samples.Length, seconds);
        var idx1 = new MemoryStream();
        using (var iw = new BinaryWriter(idx1, Encoding.ASCII, leaveOpen: true))
        {
            foreach (var (id, offset, length) in index)
            {
                iw.Write(Encoding.ASCII.GetBytes(id));
                iw.Write(0x10);
                iw.Write(offset);
                iw.Write(length);
            }
        }

        var riffSize = 4 + (8 + hdrl.Length) + (8 + movi.Length) + (8 + idx1.Length);
        writer.Write("RIFF"u8);
        writer.Write((int)riffSize);
        writer.Write("AVI "u8);
        writer.Write("LIST"u8);
        writer.Write(hdrl.Length);
        writer.Write(hdrl);
        writer.Write("LIST"u8);
        writer.Write((int)movi.Length);
        writer.Write(movi.ToArray());
        Chunk(writer, "idx1", idx1.ToArray());
    }

    private static byte[] Hdrl(int sampleRate, int frames, int seconds)
    {
        var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer, Encoding.ASCII);
        w.Write("hdrl"u8);

        var avih = new MemoryStream();
        using (var a = new BinaryWriter(avih, Encoding.ASCII, leaveOpen: true))
        {
            a.Write(1_000_000);              // microseconds per frame: 1 fps
            a.Write(FrameBytes + (sampleRate * 2));
            a.Write(0);
            a.Write(0x10);                   // AVIF_HASINDEX
            a.Write(seconds);
            a.Write(0);
            a.Write(2);                      // streams
            a.Write(sampleRate * 2);
            a.Write(Width);
            a.Write(Height);
            a.Write(new byte[16]);
        }

        Chunk(w, "avih", avih.ToArray());
        List(w, "strl", Strh("vids", "DIB ", scale: 1, rate: 1, length: seconds, sampleSize: 0, buffer: FrameBytes), Bitmap());
        List(w, "strl", Strh("auds", "\0\0\0\0", scale: 2, rate: sampleRate * 2, length: frames, sampleSize: 2, buffer: sampleRate * 2), WaveFormat(sampleRate));
        w.Flush();
        return buffer.ToArray();
    }

    private static (string Id, byte[] Data) Strh(string type, string handler, int scale, int rate, int length, int sampleSize, int buffer)
    {
        var data = new MemoryStream();
        using (var s = new BinaryWriter(data, Encoding.ASCII, leaveOpen: true))
        {
            s.Write(Encoding.ASCII.GetBytes(type));
            s.Write(Encoding.ASCII.GetBytes(handler));
            s.Write(0);
            s.Write((short)0);
            s.Write((short)0);
            s.Write(0);
            s.Write(scale);
            s.Write(rate);
            s.Write(0);
            s.Write(length);
            s.Write(buffer);
            s.Write(-1);
            s.Write(sampleSize);
            s.Write((short)0);
            s.Write((short)0);
            s.Write((short)(type == "vids" ? Width : 0));
            s.Write((short)(type == "vids" ? Height : 0));
        }

        return ("strh", data.ToArray());
    }

    private static (string Id, byte[] Data) Bitmap()
    {
        var data = new MemoryStream();
        using (var b = new BinaryWriter(data, Encoding.ASCII, leaveOpen: true))
        {
            b.Write(40);
            b.Write(Width);
            b.Write(Height);
            b.Write((short)1);
            b.Write((short)24);
            b.Write(0);
            b.Write(FrameBytes);
            b.Write(0);
            b.Write(0);
            b.Write(0);
            b.Write(0);
        }

        return ("strf", data.ToArray());
    }

    private static (string Id, byte[] Data) WaveFormat(int sampleRate)
    {
        var data = new MemoryStream();
        using (var f = new BinaryWriter(data, Encoding.ASCII, leaveOpen: true))
        {
            f.Write((short)1);
            f.Write((short)1);
            f.Write(sampleRate);
            f.Write(sampleRate * 2);
            f.Write((short)2);
            f.Write((short)16);
            f.Write((short)0);
        }

        return ("strf", data.ToArray());
    }

    private static void List(BinaryWriter writer, string type, params (string Id, byte[] Data)[] chunks)
    {
        var body = new MemoryStream();
        using (var b = new BinaryWriter(body, Encoding.ASCII, leaveOpen: true))
        {
            b.Write(Encoding.ASCII.GetBytes(type));
            foreach (var (id, data) in chunks)
            {
                Chunk(b, id, data);
            }
        }

        writer.Write("LIST"u8);
        writer.Write((int)body.Length);
        writer.Write(body.ToArray());
    }

    private static void Chunk(BinaryWriter writer, string id, byte[] data)
    {
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(data.Length);
        writer.Write(data);
        if (data.Length % 2 == 1)
        {
            writer.Write((byte)0);
        }
    }
}
