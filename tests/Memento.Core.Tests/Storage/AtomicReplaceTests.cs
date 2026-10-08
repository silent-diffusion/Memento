using System.Diagnostics;
using Memento.Core.Storage;
using Memento.Core.Storage.Interop;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Storage;

public sealed class AtomicReplaceTests : IDisposable
{
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNotSupported = 50;
    private const int ErrorSharingViolation = 32;

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task ReplacesTheTargetWhileAShareDeleteReaderHoldsIt()
    {
        var (source, target) = await PairAsync("new", "old");

        using (var held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            await AtomicReplace.ReplaceAsync(source, target, CancellationToken.None);

            using var reader = new StreamReader(held, leaveOpen: true);
            Assert.Equal("old", await reader.ReadToEndAsync());
        }

        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task CreatesTheTargetWhenItDoesNotExist()
    {
        var source = _directory.File("a.json.tmp");
        var target = _directory.File("a.json");
        await File.WriteAllTextAsync(source, "new");

        await AtomicReplace.ReplaceAsync(source, target, CancellationToken.None);

        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(source));
    }

    [Theory]
    [InlineData(ErrorInvalidParameter)]
    [InlineData(ErrorNotSupported)]
    public async Task FallsBackToFileMoveWhenPosixRenamesAreUnsupported(int error)
    {
        var (source, target) = await PairAsync("new", "old");
        var calls = 0;

        await AtomicReplace.ReplaceAsync(source, target, (_, _) => { calls++; return error; }, AtomicReplace.DefaultBudget, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task TheFallbackWaitsOutABriefLock()
    {
        var (source, target) = await PairAsync("new", "old");

        // A reader without share delete blocks File.Move; it lets go after 300 ms, beyond the old 250 ms budget.
        var held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var release = Task.Run(async () =>
        {
            await Task.Delay(300);
            await held.DisposeAsync();
        });

        await AtomicReplace.ReplaceAsync(source, target, (_, _) => ErrorNotSupported, AtomicReplace.DefaultBudget, CancellationToken.None);
        await release;

        Assert.Equal("new", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task RetriesTransientNativeFailures()
    {
        var (source, target) = await PairAsync("new", "old");
        var calls = 0;

        await AtomicReplace.ReplaceAsync(
            source,
            target,
            (from, to) => ++calls < 4 ? ErrorSharingViolation : PosixRename.Replace(from, to),
            AtomicReplace.DefaultBudget,
            CancellationToken.None);

        Assert.Equal(4, calls);
        Assert.Equal("new", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task GivesUpWithASpecificErrorWhenTheTargetStaysLocked()
    {
        var (source, target) = await PairAsync("new", "old");
        var budget = TimeSpan.FromMilliseconds(300);
        var stopwatch = Stopwatch.StartNew();

        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var error = await Assert.ThrowsAsync<IOException>(
                () => AtomicReplace.ReplaceAsync(source, target, PosixRename.Replace, budget, CancellationToken.None));

            Assert.Contains("a.json", error.Message, StringComparison.Ordinal);
            Assert.Contains("unchanged", error.Message, StringComparison.Ordinal);
            Assert.Equal(32, error.HResult & 0xFFFF);
        }

        Assert.True(stopwatch.Elapsed >= budget);
        Assert.Equal("old", await File.ReadAllTextAsync(target));
        Assert.Equal("new", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task AMissingSourceIsReportedAsMissing()
    {
        var target = _directory.File("a.json");
        await File.WriteAllTextAsync(target, "old");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => AtomicReplace.ReplaceAsync(_directory.File("missing.tmp"), target, CancellationToken.None));
        Assert.Equal("old", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public void TheRenameInformationMatchesTheWindowsLayout()
    {
        var buffer = PosixRename.RenameInformation(@"C:\x", 0x3);

        // Flags, a pointer-aligned null RootDirectory, the name's byte length, the UTF-16 name, a null.
        var nameOffset = (2 * IntPtr.Size) + sizeof(uint);
        Assert.Equal(nameOffset + 8 + 2, buffer.Length);
        Assert.Equal(3u, BitConverter.ToUInt32(buffer, 0));
        Assert.All(buffer[IntPtr.Size..(2 * IntPtr.Size)], b => Assert.Equal(0, b));
        Assert.Equal(8u, BitConverter.ToUInt32(buffer, 2 * IntPtr.Size));
        Assert.Equal(@"C:\x", System.Text.Encoding.Unicode.GetString(buffer, nameOffset, 8));
        Assert.Equal(0, buffer[^1]);
        Assert.Equal(0, buffer[^2]);
    }

    [Fact]
    public void LongPathsGetTheExtendedPrefix()
    {
        var shortPath = Path.Combine(_directory.Path, "a.json");
        var longPath = Path.Combine(_directory.Path, new string('d', 250), "a.json");

        Assert.Equal(Path.GetFullPath(shortPath), PosixRename.ToWin32Path(shortPath));
        Assert.Equal(@"\\?\" + Path.GetFullPath(longPath), PosixRename.ToWin32Path(longPath));
    }

    private async Task<(string Source, string Target)> PairAsync(string sourceText, string targetText)
    {
        var source = _directory.File("a.json.tmp");
        var target = _directory.File("a.json");
        await File.WriteAllTextAsync(source, sourceText);
        await File.WriteAllTextAsync(target, targetText);
        return (source, target);
    }
}
