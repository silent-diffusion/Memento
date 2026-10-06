namespace Memento.Audio.Tests;

/// <summary>A unique scratch folder under %TEMP%, deleted on dispose. Tests write only synthetic audio here.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "memento-audio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A reader still holds a file; the OS temp cleanup will get it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
