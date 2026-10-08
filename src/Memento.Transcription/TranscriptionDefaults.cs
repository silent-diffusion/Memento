namespace Memento.Transcription;

/// <summary>Fixed engine choices (ENGINE-NOTES.md §D, §E, §K).</summary>
public static class TranscriptionDefaults
{
    /// <summary>large-v3-turbo returns lower-case unpunctuated text without a prompt; this short one fixes it.</summary>
    public const string Prompt = "Hello, and welcome. This is a recording of a conversation, with punctuation.";

    public const string RuntimeVulkan = Core.Workers.WorkerRuntimes.Vulkan;
    public const string RuntimeCpu = Core.Workers.WorkerRuntimes.Cpu;

    /// <summary>
    /// sherpa-onnx clustering threshold: 0.8 separated two readers exactly and kept one reader whole (§E). On real
    /// meetings it splits people into many voices (114 on a 92-minute recording) but keeps each voice pure (99.7 %),
    /// which is what the host's grouping needs; 1.0 and 1.2 merge different people inside the diarizer, where nothing can
    /// part them again (§K).
    /// </summary>
    public const float ClusteringThreshold = 0.8f;

    /// <summary>
    /// Auto: two main voices on one track whose embeddings are at least this alike (cosine) are one person. 0.62–0.69
    /// gave the same, right result on both measured meetings; 0.6 merged two people and 0.72 split one (§K).
    /// </summary>
    public const double JoinSimilarity = 0.66;

    /// <summary>A voice with less speech than this (seconds of the lines it won, or 5 % of all speech if less) is folded in last.</summary>
    public const double MinSpeakerSeconds = 30;

    /// <summary>Auto: a little voice at least this alike to a main voice on its track is that person; one less alike stays apart.</summary>
    public const double FoldSimilarity = 0.2;

    public const int DiarizationThreads = 4;

    /// <summary>Processor threads for Whisper on the CPU: all physical cores, at most 8.</summary>
    public static int CpuThreads => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
}
