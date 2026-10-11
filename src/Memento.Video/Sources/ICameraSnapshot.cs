namespace Memento.Video.Sources;

/// <summary>A camera source that can copy its latest picture to managed memory: the Recording screen's thumbnail of a camera being recorded.</summary>
internal interface ICameraSnapshot
{
    /// <summary>A copy of the latest picture as NV12; <c>null</c> before the first one.</summary>
    Export.Nv12Frame? Snapshot();
}
