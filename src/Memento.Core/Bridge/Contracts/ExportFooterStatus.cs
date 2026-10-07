namespace Memento.Core.Bridge.Contracts;

/// <summary>The export part of the status footer: "Exporting {title} · 42%".</summary>
public sealed record ExportFooterStatus(bool Active, int? Percent, string? Title)
{
    public static ExportFooterStatus Idle { get; } = new(false, null, null);
}
