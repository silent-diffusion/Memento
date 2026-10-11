using System.IO.MemoryMappedFiles;

namespace Memento.Video.Sources;

/// <summary>
/// Whether a virtual camera's app is sending it pictures. A virtual camera's filter never goes away when its app stops:
/// OBS's keeps delivering a placeholder picture, so neither DirectShow nor the frame rate says the camera stopped.
/// For the OBS Virtual Camera (filter CLSID <c>{A3FCE0F5-3493-419F-958A-ABA1250EC20B}</c>) Memento reads the header of
/// the shared-memory queue OBS writes into (<c>OBSVirtualCamVideo</c>: write index at byte 0, state at byte 8,
/// 2 = ready, 3 = stopping): the camera is live while the state is ready and the write index moves; a missing queue, any
/// other state, or an index that has not moved for <see cref="StallAfter"/> (OBS was closed or crashed) means stopped.
/// The queue is opened read-only for each check and closed at once, so OBS can always recreate it. ENGINE-NOTES.md §R.
/// </summary>
internal sealed class VirtualCameraWatch
{
    public const string ObsClsid = "{A3FCE0F5-3493-419F-958A-ABA1250EC20B}";
    public const string ObsQueueName = "OBSVirtualCamVideo";
    public const uint StateReady = 2;

    public static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(2);

    private readonly Func<(uint WriteIndex, uint State)?> _read;
    private readonly Func<long> _now;
    private uint? _lastIndex;
    private long _lastMove;

    /// <param name="app">The app named in messages ("OBS").</param>
    /// <param name="read">Reads the queue header; null when the queue does not exist.</param>
    /// <param name="now">Milliseconds on a monotonic clock.</param>
    public VirtualCameraWatch(string app, Func<(uint WriteIndex, uint State)?> read, Func<long> now)
    {
        App = app;
        _read = read;
        _now = now;
    }

    /// <summary>The app that feeds the camera, for "Start the virtual camera in OBS".</summary>
    public string App { get; }

    /// <summary>The watch for a camera id, or null for a camera Memento does not know how to watch.</summary>
    public static VirtualCameraWatch? For(VideoSourceId id) =>
        id.Backend == CameraBackend.DirectShow && id.Key.StartsWith(CameraListMerger.DirectShowPrefix + "clsid:" + ObsClsid, StringComparison.OrdinalIgnoreCase)
            ? new VirtualCameraWatch("OBS", () => ReadQueue(ObsQueueName), () => Environment.TickCount64)
            : null;

    /// <summary>True while the app sends pictures. Called every half second from the source's watchdog.</summary>
    public bool IsSending()
    {
        var header = _read();
        var now = _now();
        if (header is not { State: StateReady } live)
        {
            _lastIndex = null;
            return false;
        }

        if (_lastIndex != live.WriteIndex)
        {
            _lastIndex = live.WriteIndex;
            _lastMove = now;
            return true;
        }

        return now - _lastMove < (long)StallAfter.TotalMilliseconds;
    }

    private static (uint WriteIndex, uint State)? ReadQueue(string name)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 12, MemoryMappedFileAccess.Read);
            return (view.ReadUInt32(0), view.ReadUInt32(8));
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
