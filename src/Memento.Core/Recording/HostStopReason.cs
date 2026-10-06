namespace Memento.Core.Recording;

/// <summary>Why a session stopped without the user pressing Stop.</summary>
public enum HostStopReason
{
    DiskFull,
    DeviceLost,
    Error,
}
