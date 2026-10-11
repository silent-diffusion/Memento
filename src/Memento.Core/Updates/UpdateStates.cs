namespace Memento.Core.Updates;

/// <summary>Values of <see cref="Bridge.Contracts.UpdateStatus.State"/>.</summary>
public static class UpdateStates
{
    public const string Unavailable = "unavailable";
    public const string Idle = "idle";
    public const string Checking = "checking";
    /// <summary>"Check now" found a newer version; it downloads only when the person chooses "Update now" (2.1.0).</summary>
    public const string Available = "available";
    public const string Downloading = "downloading";
    public const string Ready = "ready";
    public const string Failed = "failed";
}
