namespace Memento.AI.Server;

/// <summary>What "Test connection" found at a server address (<see cref="ServerProbe"/>).</summary>
/// <param name="Reachable">Something answered over HTTP.</param>
/// <param name="ServerName">"Ollama 0.12.3", "LM Studio", "vLLM", "llama.cpp server", the <c>Server</c> header, or the host and port.</param>
/// <param name="Models">The ids <c>GET /v1/models</c> listed, sorted.</param>
/// <param name="Code"><c>null</c> when it worked, else the <c>ai.*</c> code.</param>
/// <param name="Detail">The sentence for the page.</param>
public sealed record ServerProbeOutcome(bool Reachable, string? ServerName, IReadOnlyList<string> Models, string? Code, string Detail);
