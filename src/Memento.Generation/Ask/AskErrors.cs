using System.Globalization;
using Memento.Core.Bridge;

namespace Memento.Generation.Ask;

/// <summary>The Ask bridge errors with their copy (DESIGN.md §17: name the thing, say what is safe, then the fix).</summary>
public static class AskErrors
{
    public static BridgeException EmptyQuestion() => new(BridgeErrorCodes.InvalidParams, "Type a question first. Nothing was sent.", "question");

    public static BridgeException QuestionTooLong(int length, int limit) => new(
        BridgeErrorCodes.InvalidParams,
        string.Create(CultureInfo.InvariantCulture, $"The question is {length:N0} characters long; Ask takes up to {limit:N0}. Nothing was sent. Shorten it, or ask in two parts."),
        "question");

    public static BridgeException UnknownProvider(string id) => new(BridgeErrorCodes.InvalidParams, $"There is no AI provider called \"{id}\". Nothing was sent. Choose one from the list.", id);

    public static BridgeException AiDisabled(string provider) => new(
        DomainErrorCodes.AiDisabled,
        $"{provider} is an external service, and external AI is off, so nothing was sent. Turn on \"Allow external AI services\" in Settings › AI and privacy, or ask the local model.",
        provider);

    public static BridgeException ServerDisabled(string address) => new(
        DomainErrorCodes.AiDisabled,
        $"The server at {address} is not on this PC, and external AI is off, so nothing was sent. Turn on \"Allow external AI services\" in Settings › AI and privacy, or use a server on this PC or the local model.",
        address);

    public static BridgeException NoTranscript(string title) => new(
        DomainErrorCodes.AskNoTranscript,
        $"\"{title}\" has no transcript yet, so there is nothing to answer from. Nothing was sent. Wait until transcription finishes, then ask again.");

    public static BridgeException TranscriptNotShared(string provider) => new(
        DomainErrorCodes.AskNoTranscript,
        $"Settings › AI and privacy does not allow the transcript to be sent to external services, so {provider} cannot answer about it. Nothing was sent. Allow the transcript there, or ask the local model.",
        provider);

    public static BridgeException Busy() => new(
        DomainErrorCodes.AskBusy,
        "Another question is still being answered. Nothing was sent. Wait for the answer, or stop it, then ask again.");

    public static BridgeException NotFound(string jobId) => new(
        DomainErrorCodes.AskNotFound,
        "That question is no longer being answered; it may have finished or been stopped. Nothing was changed.",
        jobId);

    public static BridgeException NewerVersion() => new(
        DomainErrorCodes.AskNewerVersion,
        "This recording's Ask conversation was saved by a newer version of Memento, so this version leaves it as it is and adds nothing to it. Nothing was sent. Update Memento to continue the conversation.");

    public static BridgeException ClearWhileAnswering() => new(
        DomainErrorCodes.AskBusy,
        "A question about this recording is still being answered, so the conversation was not cleared. Stop the answer first, then clear.");
}