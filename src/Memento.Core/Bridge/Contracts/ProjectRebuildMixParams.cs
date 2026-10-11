namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>project.rebuildMix</c> (after 2.0.0-beta.1).</summary>
public sealed record ProjectRebuildMixParams
{
    public required string RecordingId { get; init; }

    /// <summary><c>true</c>: lift a quiet track to the loudest one's speech level; <c>false</c>: sum the tracks as recorded.</summary>
    public required bool EvenLevels { get; init; }
}
