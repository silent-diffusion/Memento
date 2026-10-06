namespace Memento.Audio.Writing;

/// <summary>Where and how a <see cref="TrackWriter"/> writes.</summary>
/// <param name="Directory">The project's <c>tracks/</c> folder.</param>
/// <param name="FileStem">File name without extension, e.g. <c>mic</c> → <c>mic.wav</c>, <c>mic.part2.wav</c>.</param>
/// <param name="InputFormat">Format of the samples handed to <see cref="TrackWriter.Write"/>.</param>
public sealed record TrackWriterOptions(string Directory, string FileStem, AudioFormat InputFormat)
{
    public long RolloverBytes { get; init; } = RollingWavWriter.DefaultRolloverBytes;

    /// <summary>How often the managed buffer is handed to the OS (caps crash loss).</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(1);

    public int BufferSize { get; init; } = StreamingWavWriter.DefaultBufferSize;

    /// <summary>Checkpoints call <c>FlushFileBuffers</c> (survive power loss). Tests may turn this off.</summary>
    public bool DurableCheckpoints { get; init; } = true;

    /// <summary>Clock in <see cref="QpcClock"/> ticks; replaceable for tests.</summary>
    public Func<long> Clock { get; init; } = static () => QpcClock.Now;
}
