namespace Memento.Core.Bridge.Contracts;

/// <summary>One module card of a template (BRIDGE.md M4).</summary>
/// <param name="Id">Unique within the template; becomes the document module's id.</param>
/// <param name="Module">The module type id (<c>actionItems</c>).</param>
/// <param name="Length"><c>short</c>, <c>medium</c> or <c>long</c>.</param>
/// <param name="TextSize"><c>smaller</c>, <c>normal</c> or <c>larger</c>.</param>
/// <param name="CustomTitle">The heading; <c>null</c> uses the catalog name.</param>
/// <param name="CustomText">Custom text modules only: the text placed as written.</param>
public sealed record ModuleSettings(string Id, string Module, string Instructions, string Length, string TextSize, bool LinkToTranscript, string? CustomTitle, string? CustomText);
