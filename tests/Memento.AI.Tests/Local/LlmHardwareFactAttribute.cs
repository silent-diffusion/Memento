namespace Memento.AI.Tests.Local;

/// <summary>
/// A fact that loads a real GGUF model; skipped unless the file is in <c>MEMENTO_LLM_MODELS</c> (default: the app's
/// <c>%LOCALAPPDATA%\Memento\models\llama</c>). A test that needs the Vulkan build is skipped when
/// <c>MEMENTO_LLM_BACKEND=cpu</c> chose the CPU build for this test process (llama.cpp loads once per process).
/// Run with <c>dotnet test --filter Category=Hardware</c>, once per backend.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LlmHardwareFactAttribute : FactAttribute
{
    public LlmHardwareFactAttribute(string modelFile, bool needsVulkan = false)
    {
        if (!File.Exists(LlmHardware.ModelPath(modelFile)))
        {
            Skip = $"Model {modelFile} is not in {LlmHardware.ModelsRoot} (set MEMENTO_LLM_MODELS).";
        }
        else if (needsVulkan && !LlmHardware.UseVulkan)
        {
            Skip = "MEMENTO_LLM_BACKEND=cpu: this test process uses the CPU build.";
        }
    }
}
