namespace Memento.Core.Bridge;

/// <summary>Error codes the router itself produces. Methods add their own <c>area.reason</c> codes.</summary>
public static class BridgeErrorCodes
{
    public const string InvalidJson = "bridge.invalidJson";
    public const string InvalidRequest = "bridge.invalidRequest";
    public const string UnknownMethod = "bridge.unknownMethod";
    public const string InvalidParams = "bridge.invalidParams";
    public const string Cancelled = "bridge.cancelled";
    public const string Internal = "bridge.internal";
}
