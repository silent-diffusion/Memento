using System.Runtime.InteropServices;

namespace Memento.Audio.Capture.Interop;

/// <summary>Reads and builds <c>WAVEFORMATEX</c> / <c>WAVEFORMATEXTENSIBLE</c> in native memory.</summary>
internal static class NativeWaveFormat
{
    private const ushort TagPcm = 1;
    private const ushort TagIeeeFloat = 3;
    private const ushort TagExtensible = 0xFFFE;
    private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    public static AudioFormat Read(IntPtr format)
    {
        var tag = (ushort)Marshal.ReadInt16(format, 0);
        var channels = (ushort)Marshal.ReadInt16(format, 2);
        var rate = Marshal.ReadInt32(format, 4);
        var bits = (ushort)Marshal.ReadInt16(format, 14);
        var cbSize = (ushort)Marshal.ReadInt16(format, 16);
        switch (tag)
        {
            case TagPcm:
                return new AudioFormat(rate, channels, bits, AudioSampleEncoding.Pcm);
            case TagIeeeFloat:
                return new AudioFormat(rate, channels, bits, AudioSampleEncoding.IeeeFloat);
            case TagExtensible when cbSize >= 22:
                var valid = (ushort)Marshal.ReadInt16(format, 18);
                var subtype = Marshal.PtrToStructure<Guid>(format + 24);
                if (subtype == SubtypeIeeeFloat)
                {
                    return new AudioFormat(rate, channels, bits, AudioSampleEncoding.IeeeFloat, valid);
                }

                if (subtype == SubtypePcm)
                {
                    return new AudioFormat(rate, channels, bits, AudioSampleEncoding.Pcm, valid);
                }

                throw new NotSupportedException($"The device mix format uses subtype {subtype}, which cannot be recorded.");
            default:
                throw new NotSupportedException($"The device mix format uses format tag {tag}, which cannot be recorded.");
        }
    }

    /// <summary>Allocates a plain <c>WAVEFORMATEX</c> (cbSize 0) for <paramref name="format"/>; free with <see cref="Marshal.FreeHGlobal"/>.</summary>
    public static IntPtr Allocate(AudioFormat format)
    {
        var p = Marshal.AllocHGlobal(18);
        Marshal.WriteInt16(p, 0, (short)(format.IsFloat ? TagIeeeFloat : TagPcm));
        Marshal.WriteInt16(p, 2, (short)format.Channels);
        Marshal.WriteInt32(p, 4, format.SampleRate);
        Marshal.WriteInt32(p, 8, format.BytesPerSecond);
        Marshal.WriteInt16(p, 12, (short)format.BlockAlign);
        Marshal.WriteInt16(p, 14, (short)format.BitsPerSample);
        Marshal.WriteInt16(p, 16, 0);
        return p;
    }
}
