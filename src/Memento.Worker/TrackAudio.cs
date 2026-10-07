using Memento.Audio.Codecs;
using Memento.Core.Workers;

namespace Memento.Worker;

/// <summary>Reads a track as 16 kHz mono float, block by block (Media Foundation decode, WDL resampler).</summary>
internal static class TrackAudio
{
    public const int SampleRate = MediaFoundationDecoder.TranscriptionSampleRate;

    public static DecodedAudio Open(string path)
    {
        try
        {
            return MediaFoundationDecoder.OpenForTranscription(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            throw new WorkerFailure(WorkerErrorCodes.Audio, $"the track {Path.GetFileName(path)} could not be read ({ex.Message.TrimEnd('.')})", ex);
        }
    }

    /// <summary>The whole track (for diarization, which needs all samples at once).</summary>
    public static float[] ReadAll(string path, CancellationToken cancellationToken)
    {
        using var audio = Open(path);
        var all = new List<float>();
        var buffer = new float[SampleRate * 10];
        int n;
        while ((n = audio.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            all.AddRange(buffer.AsSpan(0, n));
        }

        return [.. all];
    }
}
