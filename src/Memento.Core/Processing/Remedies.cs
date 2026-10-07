namespace Memento.Core.Processing;

/// <summary>Remedy ids of <c>StageFailure.remedies</c> that <c>processing.retry</c> understands.</summary>
public static class Remedies
{
    /// <summary>Try the same thing again.</summary>
    public const string Retry = "retry";

    /// <summary>Run on the processor instead of the graphics card.</summary>
    public const string Cpu = "cpu";

    /// <summary><c>model:&lt;id&gt;</c>: use that transcription model.</summary>
    public const string ModelPrefix = "model:";

    public static string Model(string modelId) => ModelPrefix + modelId;

    /// <summary><c>install:&lt;id&gt;</c>: download that model again (its file was damaged); the stage runs once it is installed.</summary>
    public const string InstallPrefix = "install:";

    public static string Install(string modelId) => InstallPrefix + modelId;
}
