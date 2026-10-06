namespace Memento.Core.Recording;

/// <summary>Why a track ended before the session.</summary>
public enum TrackEndReason
{
    /// <summary>The user turned the source off.</summary>
    Disabled,

    /// <summary>The device was unplugged or the application exited.</summary>
    SourceLost,

    /// <summary>The drive filled up.</summary>
    DiskFull,

    /// <summary>Any other capture or write failure.</summary>
    Error,
}
