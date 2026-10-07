using Memento.Core.Attachments;
using Memento.Core.Projects;

namespace Memento.Core.Tests.Projects;

/// <summary>Security audit 2026-10-07, SA-06 and SA-11: file names from project.json stay inside the project.</summary>
public sealed class ProjectPathsTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "memento-tests", "Library", "projects", "20261006-100000-k3f9ab");

    [Theory]
    [InlineData("mix.flac")]
    [InlineData("tracks/mic.flac")]
    [InlineData("tracks\\mic.part2.wav")]
    [InlineData("attachments/Agenda (final).docx")]
    public void PlainRelativeNamesAreSafe(string relative)
    {
        Assert.True(ProjectPaths.IsSafeRelative(relative));
        Assert.StartsWith(Folder + Path.DirectorySeparatorChar, ProjectPaths.Resolve(Folder, relative), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..\\..\\Music\\x.wav")]
    [InlineData("tracks/../../x.wav")]
    [InlineData("./mix.flac")]
    [InlineData("C:\\Users\\Public\\x.wav")]
    [InlineData("C:x.wav")]
    [InlineData("\\\\host\\share\\x.wav")]
    [InlineData("/x.wav")]
    [InlineData("mix.flac:hidden")]
    [InlineData("tracks//mic.flac")]
    [InlineData("tracks/mic.flac.")]
    [InlineData("tracks /mic.flac")]
    [InlineData("tracks/mi\"c.flac")]
    public void AnythingElseIsRefused(string relative)
    {
        Assert.False(ProjectPaths.IsSafeRelative(relative));
        Assert.Throws<InvalidDataException>(() => ProjectPaths.Resolve(Folder, relative));
    }

    [Fact]
    public void ResolveInKeepsAFileInsideItsSubfolder()
    {
        Assert.EndsWith(Path.Combine("attachments", "a.docx"), ProjectPaths.ResolveIn(Folder, "attachments", "attachments/a.docx"), StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ProjectPaths.ResolveIn(Folder, "attachments", "tracks/mic.flac"));
        Assert.Throws<InvalidDataException>(() => ProjectPaths.ResolveIn(Folder, "attachments", "attachments-old/a.docx"));
    }

    [Fact]
    public void AManifestNamingAFileOutsideIsReported()
    {
        var safe = Manifest("tracks/mic.flac");
        Assert.Null(ProjectPaths.FirstUnsafe(safe));
        Assert.Equal("tracks[0].file", ProjectPaths.FirstUnsafe(Manifest("..\\..\\x.wav")));
        Assert.Equal("tracks[0].captureFile", ProjectPaths.FirstUnsafe(safe with { Tracks = [safe.Tracks[0] with { CaptureFile = "C:\\x.wav" }] }));
        Assert.Equal("mix.file", ProjectPaths.FirstUnsafe(safe with { Mix = new ProjectMix("..\\mix.flac", "flac", 48000, 2, 1000, "00") }));
        Assert.Equal("peaks", ProjectPaths.FirstUnsafe(safe with { Peaks = "\\\\host\\share\\peaks.json" }));
        Assert.Equal(
            "attachments[0].file",
            ProjectPaths.FirstUnsafe(safe with
            {
                Attachments = [new AttachmentRecord { Id = "f1", Name = "a.docx", File = "attachments/../../a.docx", SizeBytes = 1, Sha256 = "00", AddedAt = DateTimeOffset.UnixEpoch, Kind = AttachmentRecord.FileKind }],
            }));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con .txt", "_con .txt")]
    [InlineData("COM0", "_COM0")]
    [InlineData("LPT¹", "_LPT¹")]
    [InlineData("CONIN$", "_CONIN$")]
    [InlineData("Agenda\u202Excod.exe", "Agendaxcod.exe")]
    [InlineData("a\u200Bb", "ab")]
    public void FileNamesNeverMakeADeviceOrASpoofedName(string name, string expected) =>
        Assert.Equal(expected, FileNames.Sanitize(name, "fallback"));

    [Fact]
    public void FileNamesNeverSplitASurrogatePair()
    {
        var name = new string('a', FileNames.MaxStemLength - 1) + "😀tail";
        var sanitized = FileNames.Sanitize(name, "fallback");
        Assert.False(char.IsHighSurrogate(sanitized[^1]));
    }

    private static ProjectManifest Manifest(string trackFile) => new()
    {
        Id = "20261006-100000-k3f9ab",
        Tracks =
        [
            new ProjectTrack { Id = "mic", SourceId = "mic", SourceKind = "microphone", Name = "Microphone", File = trackFile },
        ],
    };
}
