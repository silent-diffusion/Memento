namespace Memento.Transcription.Tests;

/// <summary>
/// A fact that runs the real worker with real models; skipped unless the worker is built, the models are installed
/// (in <c>MEMENTO_MODELS_ROOT</c> or the data root) and <c>MEMENTO_SPEECH_WAV</c> names a speech recording. Run with
/// <c>dotnet test --filter Category=Hardware</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HardwareFactAttribute : FactAttribute
{
    public HardwareFactAttribute(params string[] modelFiles)
    {
        if (WorkerBuild.Executable is null)
        {
            Skip = "Memento.Worker has not been built.";
        }
        else if (WorkerBuild.SpeechWav is null)
        {
            Skip = "Set MEMENTO_SPEECH_WAV to a speech recording to run the hardware checks.";
        }
        else if (modelFiles.FirstOrDefault(f => !File.Exists(Path.Combine(WorkerBuild.ModelsRoot, f))) is { } missing)
        {
            Skip = $"Model {missing} is not installed under {WorkerBuild.ModelsRoot}.";
        }
    }
}
