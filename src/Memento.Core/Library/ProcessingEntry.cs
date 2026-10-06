using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Library;

/// <summary>A recording that is processing: its summary and every stage, <c>stored</c> included.</summary>
public sealed record ProcessingEntry(RecordingSummary Summary, IReadOnlyList<StageStatus> Stages);
