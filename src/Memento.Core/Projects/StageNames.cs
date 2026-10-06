namespace Memento.Core.Projects;

/// <summary>Processing stage names in pipeline order. Only <see cref="Stored"/> runs in M1.</summary>
public static class StageNames
{
    public const string Stored = "stored";
    public const string Transcript = "transcript";
    public const string Speakers = "speakers";
    public const string Minutes = "minutes";
}
