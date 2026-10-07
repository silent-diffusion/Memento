namespace Memento.AI.Local;

/// <summary>Where a local model may run (<see cref="LocalLlmJob.Device"/>).</summary>
public static class LocalLlmDevices
{
    /// <summary>The graphics card when the model fits in its free memory, otherwise the processor.</summary>
    public const string Auto = "auto";

    /// <summary>The graphics card only; fails with <see cref="AiErrorCodes.NotEnoughVram"/> when the model does not fit.</summary>
    public const string Gpu = "gpu";

    /// <summary>The processor only.</summary>
    public const string Cpu = "cpu";

    public static bool IsValid(string? device) => device is Auto or Gpu or Cpu;
}
