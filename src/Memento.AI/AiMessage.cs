namespace Memento.AI;

/// <summary>One conversation turn: plain text only (audio and video are never sent anywhere).</summary>
public sealed record AiMessage(AiRole Role, string Content)
{
    public static AiMessage User(string content) => new(AiRole.User, content);

    public static AiMessage Assistant(string content) => new(AiRole.Assistant, content);
}
