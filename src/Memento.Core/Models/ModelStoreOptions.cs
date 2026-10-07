namespace Memento.Core.Models;

/// <summary>Where installed models live: <c>%LOCALAPPDATA%\Memento\models</c> by default.</summary>
/// <param name="Root">The models folder; each engine has a subfolder.</param>
public sealed record ModelStoreOptions(string Root)
{
    public static ModelStoreOptions Default => new(AppPaths.Models);

    /// <summary>Bytes kept free beyond the model itself, so a download never fills the drive.</summary>
    public long SpaceMarginBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>How long the first answer from the download server may take.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a download may receive nothing before it counts as stalled.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
