using Memento.Audio.Sources;

namespace Memento.Audio.Tests.Sources;

public sealed class AudioSourceEnumeratorTests
{
    [Theory]
    [InlineData("USB", "USB")]
    [InlineData("BTHENUM", "Bluetooth")]
    [InlineData("HDAUDIO", "Built-in")]
    [InlineData("ROOT", "Virtual device")]
    [InlineData(null, "Audio device")]
    [InlineData("SOMETHINGELSE", "Audio device")]
    public void BusLabelsAreHumanReadable(string? bus, string expected)
    {
        Assert.Equal(expected, AudioSourceEnumerator.BusLabel(bus));
    }

    [Theory]
    [InlineData("Zoom Meeting", "Zoom Workplace", "Zoom", "Zoom Meeting")]
    [InlineData("", "Mozilla Firefox", "firefox", "Mozilla Firefox")]
    [InlineData(null, "  ", "powershell", "powershell")]
    [InlineData(null, null, null, "Process 77")]
    public void AppNamesFallBackFromDisplayNameToDescriptionToProcessName(string? display, string? description, string? process, string expected)
    {
        Assert.Equal(expected, AppIdentity.ResolveName(display, description, process, 77));
    }

    [Fact]
    public void IndirectDisplayNamesAreResolvedOrDropped()
    {
        var resolved = AppIdentity.CleanDisplayName(@"@%SystemRoot%\System32\AudioSrv.Dll,-202");

        Assert.True(resolved is null || !resolved.StartsWith('@'));
        Assert.Null(AppIdentity.CleanDisplayName("  "));
        Assert.Equal("Plain", AppIdentity.CleanDisplayName("Plain"));
    }

    [Fact]
    public void ThisProcessHasAnIconAndAnImagePath()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var exe = AppIdentity.ExecutablePath(self);
        Assert.NotNull(exe);
        Assert.Equal(exe, ProcessInfoNative.QueryImagePath(Environment.ProcessId), ignoreCase: true);

        var png = AppIdentity.IconPng(exe);
        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png![..4]);
        using var image = System.Drawing.Image.FromStream(new MemoryStream(png));
        Assert.Equal(32, image.Width);
        Assert.Equal(32, image.Height);
    }

    [Trait("Category", "Hardware")]
    [HardwareFact(needsMicrophone: true, needsRender: true)]
    public void ListsMicrophonesOutputsAndAnAppPlayingAudio()
    {
        using var player = ChildPlayer.Start(seconds: 6);
        AudioSourceInfo? app = null;
        IReadOnlyList<AudioSourceInfo> sources = [];
        for (var attempt = 0; attempt < 30 && app is null; attempt++)
        {
            Thread.Sleep(100);
            sources = new AudioSourceEnumerator().List(new AudioSourceListOptions { IncludeIcons = true });
            app = sources.FirstOrDefault(s => s.ProcessId == player.ProcessId);
        }

        var mics = sources.Where(s => s.Kind == AudioSourceKind.Microphone).ToList();
        var outputs = sources.Where(s => s.Kind == AudioSourceKind.System).ToList();
        Assert.NotEmpty(mics);
        Assert.All(mics, m => Assert.StartsWith("mic:", m.Id, StringComparison.Ordinal));
        Assert.Single(mics, m => m.IsDefault);
        Assert.Equal("mic:" + Hardware.DefaultMicrophoneId, mics.Single(m => m.IsDefault).Id);
        Assert.True(outputs[0].IsDefault);
        Assert.Equal(AudioSourceEnumerator.SystemDefaultName, outputs[0].Name);
        Assert.StartsWith("Default output, ", outputs[0].Detail, StringComparison.Ordinal);

        Assert.NotNull(app);
        Assert.Equal($"app:{player.ProcessId}", app!.Id);
        Assert.Equal(AudioSourceEnumerator.ApplicationDetail, app.Detail);
        Assert.Equal("powershell", app.ProcessName, ignoreCase: true);
        Assert.Contains("PowerShell", app.Name, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(app.IconPng);
        Assert.DoesNotContain(sources, s => s.ProcessId == 0);
        Assert.Equal(sources.Count(s => s.Kind == AudioSourceKind.Application), sources.Where(s => s.ProcessId is not null).Select(s => s.ProcessId).Distinct().Count());
        Assert.DoesNotContain(sources, s => s.ProcessId == Environment.ProcessId);
    }

    [Trait("Category", "Hardware")]
    [HardwareFact(needsMicrophone: true)]
    public void DescribeNamesAnEndpointAndAProcess()
    {
        var mic = AudioSourceEnumerator.Describe(AudioSourceId.Microphone(Hardware.DefaultMicrophoneId!));
        Assert.NotNull(mic);
        Assert.True(mic!.IsDefault);

        var self = AudioSourceEnumerator.Describe(AudioSourceId.Application(Environment.ProcessId));
        Assert.NotNull(self);
        Assert.Null(AudioSourceEnumerator.Describe(AudioSourceId.Microphone("{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}")));
    }

    [Trait("Category", "Hardware")]
    [HardwareFact(needsRender: true)]
    public async Task WatcherReportsANewAudioSessionDebounced()
    {
        using var watcher = await AudioSourceWatcher.StartAsync(TimeSpan.FromMilliseconds(150));
        var events = new List<AudioSourceChanges>();
        using var raised = new SemaphoreSlim(0);
        watcher.Changed += (_, e) =>
        {
            lock (events)
            {
                events.Add(e.Changes);
            }

            raised.Release();
        };

        using var player = ChildPlayer.Start(seconds: 4);
        var got = await raised.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(got, "no change event within 5 s of a new audio session");
        lock (events)
        {
            Assert.Contains(events, e => e.HasFlag(AudioSourceChanges.SessionCreated));
        }
    }
}
