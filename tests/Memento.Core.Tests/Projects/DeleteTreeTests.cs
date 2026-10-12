using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Projects;

/// <summary>
/// A project delete outlasts a program that holds one of its files for a moment (a virus scanner reading a file Memento
/// just wrote, more often the busier the machine): it retries, counting its waits rather than the clock.
/// </summary>
public sealed class DeleteTreeTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void AFileHeldForAMomentIsWaitedForAndTheWholeFolderGoes()
    {
        var folder = Folder();
        var held = new FileStream(Path.Combine(folder, "tracks", "mic.wav"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var waits = new List<int>();

        // The holder lets go during the first wait: ordered by the retry itself, not by time.
        LinkSafeFiles.DeleteTree(folder, keepFolder: false, wait: ms =>
        {
            waits.Add(ms);
            held.Dispose();
        });

        Assert.False(Directory.Exists(folder));
        Assert.NotEmpty(waits);
    }

    [Fact]
    public void AFileThatStaysHeldFailsAfterTheBudgetOfWaits()
    {
        var folder = Folder();
        using var held = new FileStream(Path.Combine(folder, "tracks", "mic.wav"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var waits = new List<int>();

        Assert.ThrowsAny<IOException>(() => LinkSafeFiles.DeleteTree(folder, keepFolder: false, wait: waits.Add));

        Assert.InRange(waits.Sum(), (int)LinkSafeFiles.DeleteBudget.TotalMilliseconds, (int)LinkSafeFiles.DeleteBudget.TotalMilliseconds + 100);
        Assert.True(File.Exists(Path.Combine(folder, "tracks", "mic.wav")));
    }

    private string Folder()
    {
        var folder = _directory.File("project");
        Directory.CreateDirectory(Path.Combine(folder, "tracks"));
        File.WriteAllText(Path.Combine(folder, "project.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "tracks", "mic.wav"), "RIFF");
        File.SetAttributes(Path.Combine(folder, "tracks", "mic.wav"), FileAttributes.ReadOnly);
        return folder;
    }
}
