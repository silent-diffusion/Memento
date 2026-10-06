namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>app.openExternal</c>: an <c>https:</c> link or an <c>ms-settings:</c> page.</summary>
public sealed record OpenExternalParams
{
    public required string Url { get; init; }
}
