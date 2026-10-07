using Memento.Core.Workers;

namespace Memento.Generation.Tests.Hardware;

/// <summary>Where the solution build put Memento.Worker.exe, and where the local models are installed.</summary>
internal static class WorkerBuild
{
    private static readonly Lazy<string?> Folder = new(Find);

    public static string? Executable => Folder.Value is { } d ? Path.Combine(d, WorkerLocation.ExecutableName) : null;

    /// <summary><c>MEMENTO_MODELS_ROOT</c>, or the data root's models folder (<c>%LOCALAPPDATA%\Memento\models</c>).</summary>
    public static string ModelsRoot => Environment.GetEnvironmentVariable("MEMENTO_MODELS_ROOT") is { Length: > 0 } root ? root : Core.AppPaths.Models;

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
