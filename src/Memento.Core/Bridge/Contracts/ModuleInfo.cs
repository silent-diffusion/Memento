namespace Memento.Core.Bridge.Contracts;

/// <summary>One module of the catalog (<c>modules.list</c>, BRIDGE.md M4).</summary>
/// <param name="Group"><c>structure</c>, <c>detail</c> or <c>custom</c>.</param>
/// <param name="Shape"><c>paragraph</c>, <c>list</c>, <c>table</c>, <c>chips</c>, <c>labelValue</c>, <c>quote</c>, <c>timeline</c>, <c>transcript</c> or <c>text</c>.</param>
/// <param name="Generated">Written by the AI provider (and verified); otherwise placed as data or written by the user.</param>
/// <param name="DefaultLength"><c>short</c>, <c>medium</c> or <c>long</c>.</param>
/// <param name="GroundingRule">The module's rules as one sentence each, joined; <c>null</c> when it has none.</param>
/// <param name="Description">What the module holds (the instruction a new card starts with).</param>
public sealed record ModuleInfo(string Id, string Name, string Group, string Shape, bool Generated, string DefaultLength, string? GroundingRule, string Description)
{
    /// <summary>The grounding rule ids that apply (the pipeline and the validator key on them).</summary>
    public IReadOnlyList<string> GroundingRules { get; init; } = [];

    /// <summary>Whether a new card links its points to the transcript by default.</summary>
    public bool DefaultLinkToTranscript { get; init; }

    /// <summary>Table modules: the column names.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Label/value modules: the labels.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}
