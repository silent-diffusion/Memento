namespace Memento.Transcription.Stages;

/// <summary>History stage names that are not pipeline stages.</summary>
public static class HistoryStages
{
    /// <summary>Local topic extraction after a transcription pass (BRIDGE.md M2: <c>HistoryEntry.stage</c> gains <c>topics</c>).</summary>
    public const string Topics = "topics";
}
