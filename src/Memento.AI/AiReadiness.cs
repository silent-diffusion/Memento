namespace Memento.AI;

/// <summary>The result of <see cref="IAiProvider.CheckAsync"/>: whether a generation could start now, and if not why.</summary>
/// <param name="Problem">Why it cannot (no key, model not installed, not enough video memory).</param>
/// <param name="Note">Extra facts for Settings ("runs on the graphics card, 3.9 GB free"); never content.</param>
public sealed record AiReadiness(bool IsReady, AiError? Problem = null, string? Note = null)
{
    public static AiReadiness Ready(string? note = null) => new(true, null, note);

    public static AiReadiness NotReady(AiError problem, string? note = null) => new(false, problem, note);
}
