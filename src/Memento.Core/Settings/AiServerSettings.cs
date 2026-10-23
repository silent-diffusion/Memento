using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Ai;

namespace Memento.Core.Settings;

/// <summary>
/// "Your own server" (2.5.0): an OpenAI-compatible server the user runs (Ollama, LM Studio, vLLM, a llama.cpp server),
/// with the model to ask for and the context it runs with. Its optional key lives in the DPAPI store under
/// <c>server</c>, never here. A loopback address keeps everything on the PC; any other address leaves it
/// (<see cref="ServerAddress.LeavesPc"/>).
/// </summary>
public sealed record AiServerSettings
{
    /// <summary>Ollama's own default context (unless <c>num_ctx</c> was raised), so the default errs small.</summary>
    public const int DefaultContextTokens = 8192;

    /// <summary>The smallest context Settings takes (the page clamps to the same range).</summary>
    public const int MinContextTokens = 2048;

    /// <summary>The largest context Settings takes (1M tokens; the page clamps to the same range).</summary>
    public const int MaxContextTokens = 1_048_576;

    /// <summary>Longest model name accepted (Hugging Face paths with a quantisation tag run long).</summary>
    public const int MaxModelLength = 200;

    /// <summary>The address as <see cref="ServerAddress.Text"/> stores it (<c>http://localhost:11434</c>), or <c>null</c> when none is set.</summary>
    public string? Address { get; set; }

    /// <summary>The model the server is asked for, exactly as the server names it (<c>qwen3:8b</c>), or <c>null</c>.</summary>
    public string? Model { get; set; }

    /// <summary>The context the server runs the model with; the pipeline sends at most a quarter of it as transcript per request.</summary>
    public int ContextTokens { get; set; } = DefaultContextTokens;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The parsed address, or <c>null</c> when none is set (or it does not parse).</summary>
    public ServerAddress? ParsedAddress() => ServerAddress.Parse(Address, out _);

    /// <summary>
    /// A model name as servers spell them: letters, digits and <c>. _ : / - @ +</c>, starting with a letter or digit
    /// (<c>qwen3:8b</c>, <c>hf.co/org/model-GGUF:Q4_K_M</c>, <c>qwen/qwen3-8b@q4_k_m</c>).
    /// </summary>
    public static bool IsValidModel(string? model) =>
        !string.IsNullOrEmpty(model)
        && model.Length <= MaxModelLength
        && char.IsAsciiLetterOrDigit(model[0])
        && model.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '/' or '-' or '@' or '+');

    /// <summary><paramref name="tokens"/> within <see cref="MinContextTokens"/>…<see cref="MaxContextTokens"/>.</summary>
    public static int ClampContext(int tokens) => Math.Clamp(tokens, MinContextTokens, MaxContextTokens);
}
