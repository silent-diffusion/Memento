using System.Text;
using LLama;
using LLama.Common;

namespace Memento.AI.Local;

/// <summary>
/// Exact token counts from a model's own tokenizer, loading only the vocabulary (<c>VocabOnly</c>: no weights, no
/// GPU, a few MB). Content is tokenized as plain text (no special-token parsing). For a process that does not run the
/// engine; it loads llama.cpp's CPU build, which then stays the process's backend.
/// </summary>
public sealed class LlamaTokenCounter : ITokenCounter, IDisposable
{
    private readonly LLamaWeights _vocabulary;
    private readonly object _gate = new();

    public LlamaTokenCounter(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        LlamaNative.EnsureLoaded(preferVulkan: false);
        _vocabulary = LLamaWeights.LoadFromFile(new ModelParams(modelPath) { VocabOnly = true, GpuLayerCount = 0, UseMemorymap = true });
    }

    public bool IsExact => true;

    public int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            return _vocabulary.Tokenize(text, false, false, Encoding.UTF8).Length;
        }
    }

    public void Dispose() => _vocabulary.Dispose();
}
