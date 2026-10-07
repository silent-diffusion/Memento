namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>export.run</c>. <see cref="Remember"/> stores the selection and destination as the Settings › Export defaults.</summary>
public sealed record ExportRunParams
{
    public required string RecordingId { get; init; }

    public required ExportSelection Selection { get; init; }

    public required ExportDestination Destination { get; init; }

    public bool Remember { get; init; }
}
