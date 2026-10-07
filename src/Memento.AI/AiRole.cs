namespace Memento.AI;

/// <summary>Who wrote a conversation turn. The system prompt is <see cref="AiRequest.System"/>, not a turn.</summary>
public enum AiRole
{
    User,
    Assistant,
}
