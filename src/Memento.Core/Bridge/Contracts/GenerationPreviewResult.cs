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
}
