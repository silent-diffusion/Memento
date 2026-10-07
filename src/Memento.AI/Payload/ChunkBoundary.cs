namespace Memento.AI.Payload;

/// <summary>The kind of place a chunk ends, strongest last; the chunker prefers stronger boundaries.</summary>
public enum ChunkBoundary
{
    /// <summary>Inside a segment that had to be split to fit the budget.</summary>
    WithinSegment = -1,

    /// <summary>Between two segments of the same speaker.</summary>
    Segment = 0,

    /// <summary>Where the speaker changes.</summary>
    SpeakerTurn = 1,

    /// <summary>Where a topic starts.</summary>
    Topic = 2,

    /// <summary>Where a chapter starts.</summary>
    Chapter = 3,

    /// <summary>The end of the transcript.</summary>
    End = 4,
}
