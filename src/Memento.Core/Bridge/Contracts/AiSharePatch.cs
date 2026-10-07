namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › AI and privacy › What may be shared, for <c>settings.set</c>. Omitted fields keep their value.</summary>
public sealed record AiSharePatch
{
    public bool? Transcript { get; init; }

    public bool? Details { get; init; }

    public bool? Participants { get; init; }

    public bool? Agenda { get; init; }

    public bool? Highlights { get; init; }

    public bool? Attachments { get; init; }
}
