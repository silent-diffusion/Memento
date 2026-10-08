namespace Memento.Core.Bridge.Contracts;

/// <summary>The update download the status footer shows ("Downloading Memento 0.5.1 · 42%").</summary>
public sealed record UpdateFooterStatus(bool Downloading, int? Percent, string? Version)
{
    public static UpdateFooterStatus Idle { get; } = new(false, null, null);
}
