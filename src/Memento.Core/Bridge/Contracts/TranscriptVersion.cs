namespace Memento.Core.Bridge.Contracts;

/// <summary>A kept earlier version of a transcript.</summary>
/// <param name="Reason">How that version came about: <c>transcribed</c>, <c>edited</c>, <c>restored</c> or <c>retranscribed</c>.</param>
/// <param name="Engine">"whisper.cpp large-v3-turbo", or <c>null</c> when unknown.</param>
public sealed record TranscriptVersion(string Id, DateTimeOffset At, string Reason, string? Engine, int Segments);
