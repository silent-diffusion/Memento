namespace Memento.Core.Bridge.Contracts;

/// <summary>A document style (BRIDGE.md M4).</summary>
/// <param name="UsedByTemplates">How many templates use it as their default style.</param>
/// <param name="ModifiedAt">When it was last saved; <c>null</c> for a preset that was never changed.</param>
public sealed record Style(string Id, string Name, bool BuiltIn, StyleSettings Settings, int UsedByTemplates, DateTimeOffset? ModifiedAt)
{
    /// <summary>A preset the user changed (Reset is available).</summary>
    public bool Customized { get; init; }
}
