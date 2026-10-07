using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>One cloud provider's model for <c>settings.set</c> (M4): a model id, or <c>null</c> for the default.</summary>
public sealed record AiProviderPatch
{
    public JsonElement Model { get; init; }
}
