namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>recording.sourceLost</c>: one track ended; <paramref name="Remaining"/> names the sources still recording.</summary>
public sealed record SourceLostPayload(string SessionId, string SourceId, string Name, long AtMs, IReadOnlyList<string> Remaining);
