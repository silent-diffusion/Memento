using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Export;

/// <summary>One export: its plan, where it goes, and how far it got.</summary>
public sealed class ExportJob(string id, ExportPlan plan, ExportDestination destination) : IDisposable
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    private static long s_order;

    public string Id { get; } = id;

    /// <summary>Increases with every job, so the oldest finished ones can be forgotten first.</summary>
    public long Order { get; } = Interlocked.Increment(ref s_order);

    public ExportPlan Plan { get; } = plan;

    public ExportDestination Destination { get; } = destination;

    public CancellationTokenSource Cancel { get; } = new();

    public string State { get; set; } = Running;

    public int Percent { get; set; }

    public string? CurrentFile { get; set; }

    public string? Message { get; set; }

    public string? OutputFolder { get; set; }

    public int Files { get; set; }

    public long Bytes { get; set; }

    public bool IsFinished => State is Done or Failed or Cancelled;

    public ExportProgressPayload ToPayload() =>
        new(Id, Plan.RecordingId, Percent, CurrentFile, State, Message, OutputFolder, Files, Bytes);

    public void Dispose() => Cancel.Dispose();
}
