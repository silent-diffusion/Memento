namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>video.addSource</c> (2.8.0): one more display or window to record from now.</summary>
public sealed record VideoAddSourceParams
{
    public required string SessionId { get; init; }

    /// <summary>A display or window <see cref="VideoSource.Id"/> from <c>video.listSources</c>.</summary>
    public required string SourceId { get; init; }
}
