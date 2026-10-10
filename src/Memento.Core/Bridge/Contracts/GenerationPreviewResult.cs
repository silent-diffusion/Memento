namespace Memento.Core.Bridge.Contracts;

/// <summary>"Preview exactly what will be sent" (<c>generation.preview</c>). Nothing is sent.</summary>
/// <param name="PayloadText">The preview: what is included and left out, the size and hash, then the payload text unchanged.</param>
/// <param name="Bytes">UTF-8 size of the payload text itself.</param>
/// <param name="Chunks">How many transcript chunks each module pass reads.</param>
/// <param name="InputsUsed">The ticked inputs that the Settings share switches allow (cloud providers).</param>
public sealed record GenerationPreviewResult(string PayloadText, long Bytes, int Chunks, InputSelection InputsUsed, IReadOnlyList<string> Warnings)
{
    public string? ProviderId { get; init; }

    /// <summary>The local model reads it; nothing leaves the PC.</summary>
    public bool StaysOnPc { get; init; }

    /// <summary>2.0: the provider and the model in effect.</summary>
    public PreviewProviderInfo? Provider { get; init; }

    /// <summary>2.0: Settings' "Ask before every send" (it holds cloud jobs only; the local model sends nothing).</summary>
    public bool AskBeforeSend { get; init; }

    /// <summary>2.0: every input of the checklist (and the template's instructions when it has some), ticked or not, with its size.</summary>
    public IReadOnlyList<PreviewInput> Inputs { get; init; } = [];

    /// <summary>2.0: what is held back or changed before sending, in sentences; empty when nothing is.</summary>
    public IReadOnlyList<string> Redactions { get; init; } = [];

    /// <summary>2.0: each request in pipeline order, as the provider will receive it.</summary>
    public IReadOnlyList<PreviewPass> Passes { get; init; } = [];
}
