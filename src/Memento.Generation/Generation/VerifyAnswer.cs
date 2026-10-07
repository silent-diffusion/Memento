namespace Memento.Generation.Generation;

/// <summary>The verifier's grade for one question, its one-sentence reason and, for "partly", the claim without the unsupported detail.</summary>
/// <param name="Grade"><see cref="Supported"/>, <see cref="Partly"/> or <see cref="NotSupported"/>.</param>
public sealed record VerifyAnswer(string Grade, string? Reason, string? SupportedPart)
{
    public const string Supported = "supported";
    public const string Partly = "partly";
    public const string NotSupported = "not supported";

    public bool IsSupported => Grade == Supported;
}
