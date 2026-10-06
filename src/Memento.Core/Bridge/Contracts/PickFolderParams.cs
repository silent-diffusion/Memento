namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>dialog.pickFolder</c>.</summary>
public sealed record PickFolderParams
{
    public required string Title { get; init; }

    public string? InitialPath { get; init; }
}
