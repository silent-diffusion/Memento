using System.Globalization;
using System.Text.Json;
using Memento.Core.Export;

namespace Memento.Core.Tests.M3;

/// <summary>
/// The export folder and file names against <c>ui/src/format/export-naming.cases.json</c>, the cases the UI's
/// <c>exportFolderName</c> and <c>exportFileNames</c> are tested against too, so host and UI name exports the same way.
/// </summary>
public sealed class ExportNamingSharedCasesTests
{
    private static readonly Lazy<JsonDocument> Cases = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "ui", "src", "format", "export-naming.cases.json"))));

    public static TheoryData<string, string, string> FolderCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var c in Cases.Value.RootElement.GetProperty("folders").EnumerateArray())
        {
            data.Add(c.GetProperty("title").GetString()!, c.GetProperty("createdAt").GetString()!, c.GetProperty("expected").GetString()!);
        }

        return data;
    }

    public static TheoryData<int> FileCaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Value.RootElement.GetProperty("files").GetArrayLength(); i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FolderCases))]
    public void TheFolderNameMatchesTheSharedCase(string title, string createdAt, string expected)
    {
        var at = DateTimeOffset.Parse(createdAt, CultureInfo.InvariantCulture);

        Assert.Equal(expected, ExportNaming.BaseName(title, at));
    }

    [Theory]
    [MemberData(nameof(FileCaseIndexes))]
    public void TheFileNameMatchesTheSharedCase(int index)
    {
        var c = Cases.Value.RootElement.GetProperty("files")[index];
        string Text(string name) => c.GetProperty(name).GetString()!;

        var actual = Text("kind") switch
        {
            "mix" => ExportNaming.MixFile(Text("base"), Text("format")),
            "track" => ExportNaming.TrackFile(Text("base"), Text("trackName"), Text("trackId"), Text("format")),
            "transcript" => ExportNaming.TranscriptFile(Text("base"), Text("format")),
            "details" => ExportNaming.DetailsFile(Text("base")),
            "attachment" => ExportNaming.AttachmentEntry(Text("name")),
            var kind => throw new InvalidOperationException($"Unknown case kind {kind}"),
        };

        Assert.Equal(Text("expected"), actual);
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Memento.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Memento.sln was not found above the test output folder.");
    }
}
