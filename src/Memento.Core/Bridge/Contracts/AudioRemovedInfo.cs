namespace Memento.Core.Bridge.Contracts;

/// <summary>2.1: when "Delete audio" ran and what it freed (<c>RecordingSummary.audioRemoved</c>, <c>Project.audioRemoved</c>).</summary>
public sealed record AudioRemovedInfo(DateTimeOffset At, long BytesFreed);
