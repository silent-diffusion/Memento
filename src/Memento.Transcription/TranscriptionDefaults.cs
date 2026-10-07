namespace Memento.Transcription;

/// <summary>Fixed engine choices (ENGINE-NOTES.md §D, §E).</summary>
public static class TranscriptionDefaults
{
    /// <summary>large-v3-turbo returns lower-case unpunctuated text without a prompt; this short one fixes it.</summary>
    public const string Prompt = "Hello, and welcome. This is a recording of a conversation, with punctuation.";

    public const string RuntimeVulkan = "vulkan";
    public const string RuntimeCpu = "cpu";

    /// <summary>sherpa-onnx clustering threshold: 0.8 separated two readers exactly and kept one reader whole.</summary>
    public const float ClusteringThreshold = 0.8f;

    public const int DiarizationThreads = 4;

    /// <summary>Processor threads for Whisper on the CPU: all physical cores, at most 8.</summary>
    public static int CpuThreads => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
}
