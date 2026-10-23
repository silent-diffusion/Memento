namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>ai.serverProbe</c> (2.5.0): the address Settings › AI and privacy is about to save or has saved.</summary>
public sealed record AiServerProbeParams
{
    /// <summary>An http(s) address (<c>http://localhost:11434</c>; a missing scheme reads as <c>http://</c>).</summary>
    public required string Address { get; init; }
}
