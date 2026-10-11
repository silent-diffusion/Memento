namespace Memento.Video;

/// <summary>A video source, encoder or file failed. The message follows DESIGN.md §17/§18.6: the thing, the time, what is safe, the fix.</summary>
public sealed class VideoCaptureException : Exception
{
    public VideoCaptureException()
        : this(VideoErrorCode.WriteFailed, "Video recording failed.")
    {
    }

    public VideoCaptureException(string message)
        : this(VideoErrorCode.WriteFailed, message)
    {
    }

    public VideoCaptureException(string message, Exception innerException)
        : this(VideoErrorCode.WriteFailed, message, innerException)
    {
    }

    public VideoCaptureException(VideoErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public VideoCaptureException(VideoErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public VideoErrorCode Code { get; }

    /// <summary>The other app holding a camera, or the privacy switch that is off; <c>null</c> otherwise.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// The message again with the recording position (in milliseconds), for a source that knows better wording than the
    /// session's generic one but not the time (a virtual camera whose app stopped); <c>null</c> otherwise.
    /// </summary>
    public Func<long, string>? AtPosition { get; init; }

    /// <summary>The HRESULT behind it, when Windows gave one.</summary>
    public int? HResultCode => InnerException?.HResult;
}
