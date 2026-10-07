namespace Memento.Core.Bridge.Contracts;

/// <summary>An Export dialog row with no format choice (recording details, attachments).</summary>
public sealed record ExportToggle
{
    public bool On { get; init; }
}
