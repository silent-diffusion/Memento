namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Result of <c>ai.serverProbe</c> (2.5.0, "Test connection"): whether the server answered, who it is, the models it
/// offers, and when something is wrong the code and the §17 sentence. Only <c>GET /v1/models</c> (and
/// <c>GET /api/version</c> for Ollama) are sent; no recording content.
/// </summary>
/// <param name="Reachable">The server answered over HTTP (even with an error status).</param>
/// <param name="ServerName">"Ollama 0.12.3", "LM Studio", "vLLM", "llama.cpp server", the <c>Server</c> header, or the host and port.</param>
/// <param name="Models">The model ids from <c>GET /v1/models</c>, sorted; empty when none were listed.</param>
/// <param name="Code"><c>null</c> when it worked; otherwise <c>ai.serverUnreachable</c>, <c>ai.serverNeedsKey</c>, <c>ai.serverNotCompatible</c>, <c>ai.serverInsecure</c>, <c>ai.disabled</c> or <c>ai.noModel</c> (the server lists none).</param>
/// <param name="Detail">The sentence for the page: what was found, or what is wrong and the fix (DESIGN.md §17).</param>
/// <param name="Loopback">The address is this PC.</param>
/// <param name="LeavesPc">Sending to the address takes the inputs off this PC.</param>
/// <param name="Insecure">A plain <c>http://</c> address outside this PC and the local network (refused).</param>
public sealed record AiServerProbeResult(
    bool Reachable,
    string? ServerName,
    IReadOnlyList<string> Models,
    string? Code,
    string? Detail,
    bool Loopback,
    bool LeavesPc,
    bool Insecure)
{
    public IReadOnlyList<string> Models { get; set; } = Models ?? [];
}
