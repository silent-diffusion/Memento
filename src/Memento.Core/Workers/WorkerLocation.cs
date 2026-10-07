namespace Memento.Core.Workers;

/// <summary>Where <c>Memento.Worker.exe</c> is: the <c>worker</c> folder beside the app, unless a test or tool says otherwise.</summary>
public sealed record WorkerLocation(string ExecutablePath)
{
    public const string FolderName = "worker";
    public const string ExecutableName = "Memento.Worker.exe";

    public static WorkerLocation Default => new(Path.Combine(AppContext.BaseDirectory, FolderName, ExecutableName));
}
