namespace Memento.Core.Bridge.Contracts;

/// <summary>Where an export goes. With <see cref="CreateSubfolder"/> the files go into a folder named after the recording.</summary>
public sealed record ExportDestination
{
    public required string Folder { get; init; }

    public bool CreateSubfolder { get; set; } = true;
}
