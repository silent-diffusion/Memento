using Memento.AI.Local;

namespace Memento.AI.Tests.Local;

/// <summary>Where the hardware tests find models, and the one native backend this test process loads.</summary>
internal static class LlmHardware
{
    public const string Ministral3B = "Ministral-3-3B-Instruct-2512-Q4_K_M.gguf";
    public const string Qwen4B = "Qwen3.5-4B-Q4_K_M.gguf";

    public static string ModelsRoot =>
        Environment.GetEnvironmentVariable("MEMENTO_LLM_MODELS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Memento", "models", "llama");

    public static bool UseVulkan => !string.Equals(Environment.GetEnvironmentVariable("MEMENTO_LLM_BACKEND"), "cpu", StringComparison.OrdinalIgnoreCase);

    public static string ModelPath(string file) => Path.Combine(ModelsRoot, file);

    /// <summary>Loads llama.cpp as this process's backend before any test picks one by accident.</summary>
    public static string EnsureBackend() => LlamaNative.EnsureLoaded(UseVulkan);

    public static LocalLlmJob Job(string file, string catalogId, string device, IReadOnlyList<LocalLlmPrompt> prompts, int context = 0)
    {
        var entry = LocalModelCatalog.Find(catalogId)!;
        return new LocalLlmJob
        {
            ModelPath = ModelPath(file),
            ModelId = entry.Id,
            ModelName = entry.Name,
            Profile = entry.Llm,
            Device = device,
            ContextTokens = context,
            Prompts = prompts,
        };
    }
}
