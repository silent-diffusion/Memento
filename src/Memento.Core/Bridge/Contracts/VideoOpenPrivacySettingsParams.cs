namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>video.openPrivacySettings</c>.</summary>
public sealed record VideoOpenPrivacySettingsParams
{
    /// <summary><c>camera</c> or <c>screenCapture</c>.</summary>
    public required string Which { get; init; }
}
