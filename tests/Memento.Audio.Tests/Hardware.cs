using NAudio.CoreAudioApi;

namespace Memento.Audio.Tests;

/// <summary>What the machine running the tests has; CI runners have no audio endpoints and skip hardware tests.</summary>
internal static class Hardware
{
    private static readonly Lazy<string?> DefaultMic = new(() => DefaultEndpoint(DataFlow.Capture));
    private static readonly Lazy<string?> DefaultRender = new(() => DefaultEndpoint(DataFlow.Render));

    public static string? DefaultMicrophoneId => DefaultMic.Value;

    public static string? DefaultRenderId => DefaultRender.Value;

    private static string? DefaultEndpoint(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
            {
                return null;
            }

            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
            return device.State == DeviceState.Active ? device.ID : null;
        }
#pragma warning disable CA1031 // No audio service or no devices: the hardware tests are skipped.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
