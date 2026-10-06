namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of the <c>status.footer</c> event (DESIGN.md §3 footer).</summary>
public sealed record FooterStatusPayload(EngineStatus Engine, StorageStatus Storage);
