namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>generation.start</c>. With <see cref="ConfirmationRequired"/> nothing runs until <c>generation.confirm</c>.</summary>
public sealed record GenerationStartResult(string JobId)
{
    public bool ConfirmationRequired { get; init; }

    public GenerationSendSummary? Summary { get; init; }
}
