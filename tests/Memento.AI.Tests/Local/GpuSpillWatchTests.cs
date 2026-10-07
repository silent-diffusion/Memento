using Memento.AI.Local;

namespace Memento.AI.Tests.Local;

public sealed class GpuSpillWatchTests
{
    private const long MiB = 1024 * 1024;

    [Fact]
    public void FiresOnceWhenSharedMemoryGrowsPastTheThreshold()
    {
        var memory = new ScriptedMemory(new(0, 10 * MiB), new(3000 * MiB, 50 * MiB), new(3100 * MiB, 900 * MiB), new(3100 * MiB, 1200 * MiB));
        var fired = 0;
        using var watch = new GpuSpillWatch(memory, 384 * MiB, () => fired++);

        Assert.True(watch.CanWatch);
        Assert.False(watch.Check());
        Assert.True(watch.Check());
        Assert.True(watch.Check());
        Assert.Equal(1, fired);
        Assert.True(watch.Spilled);
        Assert.Equal(1190 * MiB, watch.MaxSharedGrowthBytes);
        Assert.Equal(3100 * MiB, watch.LastDedicatedBytes);
    }

    [Fact]
    public void NormalStagingBuffersDoNotCountAsASpill()
    {
        var memory = new ScriptedMemory(new(0, 0), new(3300 * MiB, 41 * MiB), new(3450 * MiB, 99 * MiB));
        using var watch = new GpuSpillWatch(memory, 384 * MiB, () => throw new InvalidOperationException("no spill expected"));

        Assert.False(watch.Check());
        Assert.False(watch.Check());
        Assert.Equal(99 * MiB, watch.MaxSharedGrowthBytes);
    }

    [Fact]
    public void WithoutCountersTheWatchSaysSo()
    {
        using var watch = new GpuSpillWatch(new ScriptedMemory(), 384 * MiB, () => { });

        Assert.False(watch.CanWatch);
        Assert.False(watch.Check());
    }

    [Fact]
    public async Task TheTimerChecksInTheBackground()
    {
        var memory = new ScriptedMemory(new(0, 0), new(1, 2000 * MiB));
        using var spilled = new ManualResetEventSlim();
        using var watch = new GpuSpillWatch(memory, 384 * MiB, spilled.Set);

        watch.Start(TimeSpan.FromMilliseconds(20));

        Assert.True(await Task.Run(() => spilled.Wait(TimeSpan.FromSeconds(5))));
    }

    private sealed class ScriptedMemory(params GpuProcessMemorySample[] samples) : IGpuProcessMemory
    {
        private int _next;

        public GpuProcessMemorySample? Sample() =>
            samples.Length == 0 ? null : samples[Math.Min(Interlocked.Increment(ref _next) - 1, samples.Length - 1)];
    }
}
