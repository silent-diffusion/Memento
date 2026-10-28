using Memento.Core.Bridge.Contracts;

namespace Memento.Generation.Ask;

/// <summary>
/// A recording's Ask conversation on disk (2.9.0): <c>ask/conversation.json</c> in the project folder, schema v1, written
/// atomically. Holds the questions and answers only; never the material that was sent with them.
/// </summary>
public sealed record AskConversationFile(int SchemaVersion, IReadOnlyList<AskTurn> Turns)
{
    public const int CurrentSchemaVersion = 1;
}