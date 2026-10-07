using System.Diagnostics;
using Memento.Core.Library;

namespace Memento.Core.Tests.Library;

/// <summary>Security audit 2026-10-07, SA-02: what <c>https://library.memento/</c> answers.</summary>
public sealed class LibraryResourcePolicyTests : IDisposable
{
    private const string Id = "20261006-100000-k3f9ab";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "memento-tests", "policy-" + Guid.NewGuid().ToString("N"));
    private readonly string _projects;
    private readonly string _outside;

    public LibraryResourcePolicyTests()
    {
        _projects = LibraryUrls.MappedFolder(Path.Combine(_root, "Library"));
        _outside = Path.Combine(_root, "Outside");
        Directory.CreateDirectory(Path.Combine(_projects, Id));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_projects, Id, "mix.flac"), "fLaC");
        File.WriteAllText(Path.Combine(_projects, Id, "peaks.json"), "{}");
        File.WriteAllText(Path.Combine(_projects, Id, "transcript.json"), "{}");
        File.WriteAllText(Path.Combine(_outside, "mix.flac"), "secret");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A scanner may still hold a fresh junction; %TEMP% is cleaned by Windows.
        }
    }

    private string? Serve(string url) => LibraryResourcePolicy.ServableFile(_projects, new Uri(url));

    [Fact]
    public void ServesTheMixAndPeaksTheHostHandsOut()
    {
        Assert.Equal(Path.Combine(_projects, Id, "mix.flac"), Serve(LibraryUrls.ForProjectFile(Id, "mix.flac")));
        Assert.Equal(Path.Combine(_projects, Id, "peaks.json"), Serve(LibraryUrls.ForProjectFile(Id, "peaks.json")));
    }

    [Theory]
    [InlineData("https://library.memento/" + Id + "/transcript.json")]
    [InlineData("https://library.memento/" + Id + "/project.json")]
    [InlineData("https://library.memento/" + Id + "/tracks/mic.flac")]
    [InlineData("https://library.memento/" + Id + "/mix.flac:hidden")]
    [InlineData("https://library.memento/" + Id + "/mix.flac%3Ahidden")]
    [InlineData("https://library.memento/" + Id + "/mix.flac.")]
    [InlineData("https://library.memento/" + Id + "/..%5C..%5Clibrary.db")]
    [InlineData("https://library.memento/" + Id + "/%2E%2E%2Fpeaks.json")]
    [InlineData("https://library.memento/not-a-project/mix.flac")]
    [InlineData("https://library.memento/mix.flac")]
    [InlineData("https://library.memento/")]
    [InlineData("http://library.memento/" + Id + "/mix.flac")]
    [InlineData("https://library.memento:8443/" + Id + "/mix.flac")]
    [InlineData("https://app.memento/" + Id + "/mix.flac")]
    [InlineData("https://library.memento/" + Id + "/missing.flac")]
    public void RefusesEverythingElse(string url) => Assert.Null(Serve(url));

    [Fact]
    public void RefusesAProjectFolderThatIsAJunction()
    {
        const string junctionId = "20261006-100000-aaaaaa";
        if (!TryCreateJunction(Path.Combine(_projects, junctionId), _outside))
        {
            return; // mklink is unavailable on this machine; the attribute check is covered by the file case below.
        }

        Assert.True(File.Exists(Path.Combine(_projects, junctionId, "mix.flac")));
        Assert.Null(Serve(LibraryUrls.ForProjectFile(junctionId, "mix.flac")));
    }

    [Fact]
    public void RefusesAFileWithTheReparsePointAttribute()
    {
        // A symbolic file link needs a privilege tests may not have; a junction inside the project folder named like a
        // media file stands in for it (the policy checks the attribute, whatever the link kind).
        var link = Path.Combine(_projects, Id, "mix.wav");
        if (!TryCreateJunction(link, _outside))
        {
            return;
        }

        Assert.Null(Serve(LibraryUrls.ForProjectFile(Id, "mix.wav")));
    }

    private static bool TryCreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start);
        if (process is null)
        {
            return false;
        }

        process.WaitForExit(10_000);
        return process.ExitCode == 0 && Directory.Exists(link);
    }
}
