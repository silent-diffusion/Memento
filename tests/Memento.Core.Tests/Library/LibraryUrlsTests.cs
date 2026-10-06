using Memento.Core.Library;

namespace Memento.Core.Tests.Library;

/// <summary>The <c>https://library.memento/</c> virtual host: which folder it serves and the URLs the host hands out.</summary>
public sealed class LibraryUrlsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "memento-tests", "Library");

    /// <summary>
    /// What WebView2 does with a library URL: the browser canonicalizes the path (removing <c>.</c> and <c>..</c>
    /// segments; <see cref="Uri"/> follows the same URL rules), then the remaining path is resolved below the mapped folder.
    /// </summary>
    private static string Resolve(string url)
    {
        var uri = new Uri(url);
        Assert.Equal(LibraryUrls.VirtualHost, uri.Host);
        var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        return Path.GetFullPath(Path.Combine(LibraryUrls.MappedFolder(Root), relative));
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void TheMappedFolderIsTheProjectsFolderOfTheLibrary()
    {
        Assert.Equal(Path.Combine(Root, "projects"), LibraryUrls.MappedFolder(Root));
        Assert.Equal(Path.Combine(Root, "projects"), LibraryUrls.MappedFolder(Root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void TheLibraryDatabaseIsNotUnderTheMappedFolder()
    {
        var database = Path.Combine(Root, SqliteLibraryIndex.DatabaseFileName);

        Assert.False(IsInside(database, LibraryUrls.MappedFolder(Root)));
        Assert.True(IsInside(Path.Combine(Root, "projects", "20261006-100000-k3f9ab", "mix.flac"), LibraryUrls.MappedFolder(Root)));
    }

    [Fact]
    public void UrlsNameTheProjectFolderDirectlyBelowTheHost()
    {
        Assert.Equal("https://library.memento/20261006-100000-k3f9ab/mix.flac", LibraryUrls.ForProjectFile("20261006-100000-k3f9ab", "mix.flac"));
        Assert.Equal("https://library.memento/20261006-100000-k3f9ab/peaks.json", LibraryUrls.ForProjectFile("20261006-100000-k3f9ab", "peaks.json"));
        Assert.Equal("https://library.memento/20261006-100000-k3f9ab/tracks/mic.flac", LibraryUrls.ForProjectFile("20261006-100000-k3f9ab", "tracks\\mic.flac"));
    }

    [Fact]
    public void AUrlResolvesToTheFileInsideTheProjectFolder()
    {
        var url = LibraryUrls.ForProjectFile("20261006-100000-k3f9ab", "tracks/mic.flac");

        Assert.Equal(Path.Combine(Root, "projects", "20261006-100000-k3f9ab", "tracks", "mic.flac"), Resolve(url));
    }

    [Theory]
    [InlineData("Mic #1.flac", "Mic%20%231.flac")]
    [InlineData("50% mix?.flac", "50%25%20mix%3F.flac")]
    [InlineData("été.flac", "%C3%A9t%C3%A9.flac")]
    public void SegmentsAreEscaped(string file, string escaped)
    {
        Assert.Equal($"https://library.memento/20261006-100000-k3f9ab/tracks/{escaped}", LibraryUrls.ForProjectFile("20261006-100000-k3f9ab", "tracks/" + file));
    }

    [Fact]
    public void TheIdIsEscapedAsOneSegment()
    {
        Assert.Equal("https://library.memento/odd%20id%3F/mix.flac", LibraryUrls.ForProjectFile("odd id?", "mix.flac"));
    }

    [Theory]
    [InlineData("..", "mix.flac")]
    [InlineData(".", "mix.flac")]
    [InlineData("20261006-100000-k3f9ab", "../library.db")]
    [InlineData("20261006-100000-k3f9ab", "..\\..\\library.db")]
    [InlineData("20261006-100000-k3f9ab", "tracks/../../library.db")]
    [InlineData("20261006-100000-k3f9ab", "./mix.flac")]
    [InlineData("20261006-100000-k3f9ab", "...")]
    [InlineData("../20261006-100000-k3f9ab", "mix.flac")]
    [InlineData("a\\b", "mix.flac")]
    public void DotSegmentsAndSeparatorsInTheIdAreRefused(string recordingId, string file)
    {
        Assert.Throws<ArgumentException>(() => LibraryUrls.ForProjectFile(recordingId, file));
    }

    [Theory]
    [InlineData("https://library.memento/../library.db")]
    [InlineData("https://library.memento/../../library.db")]
    [InlineData("https://library.memento/20261006-100000-k3f9ab/../../library.db")]
    [InlineData("https://library.memento/%2e%2e/library.db")]
    [InlineData("https://library.memento/%2E%2E/%2E%2E/library.db")]
    [InlineData("https://library.memento/./../settings.json")]
    public void ADotDotPathCannotClimbOutOfTheProjectsFolder(string url)
    {
        var path = Resolve(url);

        Assert.True(IsInside(path, LibraryUrls.MappedFolder(Root)), path);
        Assert.NotEqual(Path.Combine(Root, SqliteLibraryIndex.DatabaseFileName), path, StringComparer.OrdinalIgnoreCase);
    }
}
