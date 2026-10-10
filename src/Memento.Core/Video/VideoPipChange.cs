namespace Memento.Core.Video;

/// <summary>From <paramref name="AtMs"/> on the recording timeline, the camera inset is on or off in <paramref name="Corner"/>.</summary>
public sealed record VideoPipChange(long AtMs, bool Enabled, string Corner)
{
    public const string TopLeft = "topLeft";
    public const string TopRight = "topRight";
    public const string BottomLeft = "bottomLeft";
    public const string BottomRight = "bottomRight";

    public static IReadOnlyList<string> Corners { get; } = [TopLeft, TopRight, BottomLeft, BottomRight];

    public static bool IsCorner(string? corner) => corner is not null && Corners.Contains(corner, StringComparer.Ordinal);
}
