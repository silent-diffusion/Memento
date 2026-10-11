namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>recording.setVideoQuality</c>: the §18.6 encoder banner's Lower quality.</summary>
public sealed record RecordingSetVideoQualityParams
{
    public required string SessionId { get; init; }

    /// <summary><c>smaller</c>, <c>balanced</c> or <c>best</c>.</summary>
    public required string Quality { get; init; }
}
