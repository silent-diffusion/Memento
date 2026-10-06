namespace Memento.Core.Projects;

/// <summary>Values of <c>StageStatus.State</c>.</summary>
public static class StageStates
{
    public const string Done = "done";
    public const string Active = "active";
    public const string Queued = "queued";
    public const string Failed = "failed";

    public static bool IsRunning(string state) => state is Active or Queued;
}
