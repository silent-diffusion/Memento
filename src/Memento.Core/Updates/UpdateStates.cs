namespace Memento.Core.Updates;

/// <summary>Values of <see cref="Bridge.Contracts.UpdateStatus.State"/>.</summary>
public static class UpdateStates
{
    public const string Unavailable = "unavailable";
    public const string Idle = "idle";
    public const string Checking = "checking";
    public const string Downloading = "downloading";
    public const string Ready = "ready";
    public const string Failed = "failed";
}
