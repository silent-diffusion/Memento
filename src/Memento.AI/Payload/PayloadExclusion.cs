namespace Memento.AI.Payload;

/// <summary>Something the recording has that is not in the payload, and why (shown in the preview: nothing is dropped silently).</summary>
public sealed record PayloadExclusion(PayloadSectionKind Kind, string Name, string Reason);
