using System.Globalization;

namespace Memento.AI.Local;

/// <summary>A spill out of the graphics card's own memory (<see cref="AiErrors.VramSpilled"/>), as the engine reports it.</summary>
public static class LocalLlmSpill
{
    private const string Prefix = "sharedGrowth=";

    /// <summary>The error is the spill watch's (shared GPU memory grew), not another video memory failure.</summary>
    public static bool Is(LocalLlmException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Code == AiErrorCodes.NotEnoughVram && exception.Error.Diagnostic?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    }

    /// <summary>How much moved to shared memory, from the spill's diagnostic (0 when it does not say).</summary>
    public static long Growth(LocalLlmException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Error.Diagnostic is { } d && d.StartsWith(Prefix, StringComparison.Ordinal)
            && long.TryParse(d.AsSpan(Prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
            ? bytes
            : 0;
    }
}
