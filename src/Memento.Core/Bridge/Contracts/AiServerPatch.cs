using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// 2.5.0: "Your own server" for <c>settings.set</c>. Each field present is changed; a JSON <c>null</c> clears the
/// address or the model (the context has no empty value). The key still changes only through <c>ai.setKey</c> /
/// <c>ai.clearKey</c> with <c>provider: 'server'</c>.
/// </summary>
public sealed record AiServerPatch
{
    /// <summary>An http(s) address (saved normalized), or <c>null</c> to clear it; omitted keeps it.</summary>
    public JsonElement Address { get; init; }

    /// <summary>The model the server is asked for, or <c>null</c> to clear it; omitted keeps it.</summary>
    public JsonElement Model { get; init; }

    /// <summary>The server's context in tokens, clamped to 2,048…1,048,576 (as the page does); omitted keeps it.</summary>
    public int? ContextTokens { get; init; }
}
