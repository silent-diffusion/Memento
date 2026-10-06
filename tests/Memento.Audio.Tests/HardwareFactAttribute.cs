namespace Memento.Audio.Tests;

/// <summary>A fact that needs real audio endpoints; skipped when the machine has none (CI).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HardwareFactAttribute : FactAttribute
{
    public HardwareFactAttribute(bool needsMicrophone = false, bool needsRender = false)
    {
        if (needsMicrophone && Hardware.DefaultMicrophoneId is null)
        {
            Skip = "No active capture endpoint on this machine.";
        }
        else if (needsRender && Hardware.DefaultRenderId is null)
        {
            Skip = "No active render endpoint on this machine.";
        }
    }
}
