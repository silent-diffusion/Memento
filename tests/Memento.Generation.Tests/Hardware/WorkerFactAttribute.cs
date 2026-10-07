namespace Memento.Generation.Tests.Hardware;

/// <summary>
/// A fact that runs the real Memento.Worker.exe; skipped when the worker has not been built, or when a model file it
/// names is not installed under <c>{models root}\llama</c>. Tests that load a model are also marked
/// <c>[Trait("Category", "Hardware")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WorkerFactAttribute : FactAttribute
{
    public WorkerFactAttribute(params string[] modelFiles)
    {
        if (WorkerBuild.Executable is null)
        {
            Skip = "Memento.Worker has not been built.";
        }
        else if (modelFiles.FirstOrDefault(f => !File.Exists(Path.Combine(WorkerBuild.ModelsRoot, "llama", f))) is { } missing)
        {
            Skip = $"Model {missing} is not installed under {Path.Combine(WorkerBuild.ModelsRoot, "llama")}.";
        }
    }
}
