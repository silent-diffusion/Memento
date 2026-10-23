using System.Collections.Concurrent;

namespace Memento.AI.Server;

/// <summary>
/// What Memento has learnt, for as long as it runs, about servers that refuse <c>response_format: json_schema</c>
/// (some vLLM and llama.cpp builds): after the first refusal every request to that address asks for <c>json_object</c>
/// with the schema in the system message instead, and the provider reports <c>SupportsJsonSchema: false</c>. Kept per
/// address (a new server at another address starts trusted again); never written anywhere.
/// </summary>
public static class ServerSchemaSupport
{
    private static readonly ConcurrentDictionary<string, bool> Refused = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The server at <paramref name="address"/> has not refused a JSON schema since Memento started.</summary>
    public static bool Supports(string address) => !Refused.ContainsKey(address);

    public static void MarkRefused(string address) => Refused[address] = true;

    /// <summary>Forgets what was learnt about <paramref name="address"/> (tests; a server that was upgraded).</summary>
    public static void Forget(string address) => Refused.TryRemove(address, out _);
}
