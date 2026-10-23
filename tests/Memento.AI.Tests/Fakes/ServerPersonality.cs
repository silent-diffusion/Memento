namespace Memento.AI.Tests.Fakes;

/// <summary>Which real server <see cref="FakeOpenAiCompatibleServer"/> behaves like.</summary>
public enum ServerPersonality
{
    /// <summary>
    /// Ollama: <c>/api/version</c>, <c>/v1/models</c> as <c>object: list</c> with <c>owned_by: library</c>, a strict JSON
    /// schema accepted, no usage chunk, <c>[DONE]</c>; an unknown model is a 404 "model … not found, try pulling it first".
    /// </summary>
    Ollama,

    /// <summary>LM Studio: <c>owned_by: organization_owner</c>, a strict JSON schema accepted, a usage chunk, then <c>[DONE]</c>.</summary>
    LmStudio,

    /// <summary>
    /// vLLM or a llama.cpp server built without schema support: a <c>json_schema</c> response format is refused with a 400
    /// that names it, <c>json_object</c> is accepted; <c>owned_by: vllm</c>, <c>Server: uvicorn</c>.
    /// </summary>
    Vllm,
}
