using Memento.Core;
using Memento.Core.Workers;
using Memento.Documents.Agenda.Ocr;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Documents.Tests.Support;

/// <summary>
/// The real Tesseract: the solution's build of Memento.Worker.exe and the English language data under
/// <c>{models root}\tesseract\eng.traineddata</c> (<c>MEMENTO_MODELS_ROOT</c>, else <c>%LOCALAPPDATA%\Memento\models</c>).
/// Tests that need it are skipped when either is missing (CI has neither).
/// </summary>
internal static class TesseractSupport
{
    private static readonly Lazy<string?> WorkerFolder = new(FindWorker);

    public static string ModelsRoot => Environment.GetEnvironmentVariable("MEMENTO_MODELS_ROOT") is { Length: > 0 } root ? root : AppPaths.Models;

    public static string LanguageData => Path.Combine(ModelsRoot, "tesseract", "eng.traineddata");

    public static string? WorkerExecutable => WorkerFolder.Value is { } folder ? Path.Combine(folder, WorkerLocation.ExecutableName) : null;

    public static string? SkipReason =>
        WorkerExecutable is null ? "Memento.Worker has not been built."
        : !File.Exists(LanguageData) ? $"Tesseract English is not installed at {LanguageData}."
        : null;

    /// <summary>The engine on the real worker, with the language data counted as installed and verified.</summary>
    public static (TesseractOcrEngine Engine, IDisposable Owner) Create()
    {
        var launcher = new ProcessWorkerLauncher(new WorkerLocation(WorkerExecutable!), NullLogger<ProcessWorkerLauncher>.Instance);
        var workers = new WorkerClient(launcher, NullLogger<WorkerClient>.Instance);
        var models = new FakeModelManager(new Dictionary<string, string> { [TesseractOcrEngine.EnglishModelId] = LanguageData });
        return (new TesseractOcrEngine(models, new WorkerTesseractRunner(workers)), new Owner(workers, launcher));
    }

    private static string? FindWorker()
    {
        if (Environment.GetEnvironmentVariable("MEMENTO_WORKER_DIR") is { Length: > 0 } forced)
        {
            return File.Exists(Path.Combine(forced, WorkerLocation.ExecutableName)) ? forced : null;
        }

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

    private sealed class Owner(WorkerClient workers, ProcessWorkerLauncher launcher) : IDisposable
    {
        public void Dispose()
        {
            workers.Dispose();
            launcher.Dispose();
        }
    }
}
