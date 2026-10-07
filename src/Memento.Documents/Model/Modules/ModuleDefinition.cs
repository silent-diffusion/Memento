namespace Memento.Documents.Model.Modules;

/// <summary>
/// One entry of the module catalog: what the Builder palette lists, what the preview skeleton draws, and what the
/// generation pipeline asks for and checks.
/// </summary>
public sealed record ModuleDefinition
{
    /// <summary>The module type id (<see cref="ModuleIds"/>).</summary>
    public required string Id { get; init; }

    /// <summary>The palette name and default heading ("Action items").</summary>
    public required string DisplayName { get; init; }

    public required PaletteGroup Group { get; init; }

    public required ContentShape Shape { get; init; }

    public ModuleLength DefaultLength { get; init; } = ModuleLength.Medium;

    public required ModuleSource Source { get; init; }

    /// <summary>The <see cref="Modules.GroundingRules"/> ids that apply.</summary>
    public IReadOnlyList<string> GroundingRules { get; init; } = [];

    /// <summary>Table shape only: the named columns ("Action", "Owner", "Due").</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Table shape only: relative column widths, one per column.</summary>
    public IReadOnlyList<double> ColumnWidths { get; init; } = [];

    /// <summary>Label/value shape only: the labels the module fills ("When", "Agenda").</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>The instruction a new card starts with in the Builder.</summary>
    public string DefaultInstructions { get; init; } = string.Empty;

    /// <summary>Whether a new card links its points back to the transcript by default.</summary>
    public bool DefaultLinkToTranscript { get; init; }

    /// <summary>Whether the content is written by the AI provider.</summary>
    public bool IsAiGenerated => Source == ModuleSource.Ai;
}
