using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.DocumentModel.Support;

/// <summary>
/// Snapshot fixtures in <c>fixtures/expected/</c> (read from the source tree). Set <c>MEMENTO_UPDATE_SNAPSHOTS=1</c> to
/// write them after an intended change; review the diff before committing.
/// </summary>
internal static class Snapshot
{
    public static bool Updating => Environment.GetEnvironmentVariable("MEMENTO_UPDATE_SNAPSHOTS") == "1";

    public static string Directory => Path.Combine(FixturePaths.SourceDirectory, "expected");

    public static string PathOf(string name) => Path.Combine(Directory, name);

    public static void Match(string name, string actual)
    {
        var path = PathOf(name);
        var normalized = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Updating)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, normalized);
            return;
        }

        Assert.True(File.Exists(path), $"The snapshot {name} is missing; run the tests with MEMENTO_UPDATE_SNAPSHOTS=1 to create it.");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, normalized);
    }

    /// <summary>Writes a binary output beside the snapshots when updating (for people to open); never compared.</summary>
    public static void Attach(string name, ReadOnlySpan<byte> bytes)
    {
        if (Updating)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllBytes(PathOf(name), bytes.ToArray());
        }
    }

    public static string Read(string name) => File.ReadAllText(PathOf(name));
}
