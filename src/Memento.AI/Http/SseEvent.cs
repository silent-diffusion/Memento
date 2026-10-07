namespace Memento.AI.Http;

/// <summary>One server-sent event: the <c>event:</c> name (may be empty) and the joined <c>data:</c> lines.</summary>
internal readonly record struct SseEvent(string Event, string Data);
