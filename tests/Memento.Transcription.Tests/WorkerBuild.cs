using Memento.Core.Workers;

namespace Memento.Transcription.Tests;

/// <summary>Where the solution build put Memento.Worker.exe, and what the hardware checks need.</summary>
internal static class WorkerBuild
{
    private static readonly Lazy<string?> Folder = new(Find);

    /// <summary>The worker's build output folder, or <c>null</c> when it has not been built.</summary>
    public static string? Directory => Folder.Value;

    public static string? Executable => Directory is { } d ? Path.Combine(d, WorkerLocation.ExecutableName) : null;

    /// <summary><c>MEMENTO_MODELS_ROOT</c>, or the data root's models folder.</summary>
    public static string ModelsRoot => Environment.GetEnvironmentVariable("MEMENTO_MODELS_ROOT") is { Length: > 0 } root
        ? root
        : Core.AppPaths.Models;

    /// <summary><c>MEMENTO_SPEECH_WAV</c>: a speech recording (16 kHz mono WAV or any decodable file) for the hardware checks.</summary>
    public static string? SpeechWav => Environment.GetEnvironmentVariable("MEMENTO_SPEECH_WAV") is { Length: > 0 } wav && File.Exists(wav) ? wav : null;

    private static string? Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Memento.sln")))
            {
                foreach (var configuration in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(directory.FullName, "src", "Memento.Worker", "bin", configuration, "net8.0-windows", "win-x64");
                    if (File.Exists(Path.Combine(candidate, WorkerLocation.ExecutableName)))
                    {
                        return candidate;
                    }
                }

                return null;
            }
        }

        return null;
    }
}
