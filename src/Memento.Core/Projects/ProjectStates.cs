namespace Memento.Core.Projects;

/// <summary>Values of <see cref="ProjectManifest.State"/> (and <c>RecordingSummary.state</c>).</summary>
public static class ProjectStates
{
    public const string Recording = "recording";
    public const string Finalizing = "finalizing";
    public const string Ready = "ready";
    public const string Recovered = "recovered";
    public const string Failed = "failed";
}
