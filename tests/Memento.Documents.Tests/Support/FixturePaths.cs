namespace Memento.Documents.Tests.Support;

/// <summary>Where the agenda fixtures are: copied beside the test assembly, and in the source tree for regeneration.</summary>
internal static class FixturePaths
{
    public static string OutputDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>The test project's own fixtures folder, found by walking up from the test assembly.</summary>
    public static string SourceDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Memento.Documents.Tests.csproj")))
            {
                directory = directory.Parent;
            }

            return directory is null
                ? throw new DirectoryNotFoundException("The Memento.Documents.Tests project folder was not found above the test assembly.")
                : Path.Combine(directory.FullName, "fixtures");
        }
    }

    public static string Output(string name) => Path.Combine(OutputDirectory, name);

    /// <summary>Every fixture that has an expected-items file, by file name.</summary>
    public static IReadOnlyList<string> All() =>
        Directory.EnumerateFiles(OutputDirectory)
            .Where(f => !f.EndsWith(".expected", StringComparison.Ordinal) && File.Exists(f + ".expected"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();

    public static bool IsImage(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff";

    public static bool IsPasted(string name) => name.Contains(".paste.", StringComparison.Ordinal);
}
