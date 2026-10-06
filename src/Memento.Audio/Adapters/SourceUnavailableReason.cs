using System.Globalization;
using Memento.Audio.Capture;

namespace Memento.Audio.Adapters;

/// <summary>
/// The short reason Core puts in parentheses after a source's name ("Microphone Array could not be opened
/// (<i>reason</i>), so the recording did not start …"), derived from the audio layer's full message.
/// </summary>
internal static class SourceUnavailableReason
{
    private const int AccessDenied = unchecked((int)0x80070005);

    public static string From(AudioSourceUnavailableException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.Message;
        if (exception.HResult == AccessDenied || message.Contains("denied access", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows is blocking microphone access for desktop apps; allow it in Settings › Privacy & security › Microphone";
        }

        if (message.Contains("no longer running", StringComparison.OrdinalIgnoreCase))
        {
            return "the app is no longer running";
        }

        if (message.Contains("no longer connected", StringComparison.OrdinalIgnoreCase) || message.Contains("disabled or unplugged", StringComparison.OrdinalIgnoreCase))
        {
            return "it is disconnected or disabled";
        }

        if (message.Contains("version 2004", StringComparison.OrdinalIgnoreCase))
        {
            return "per-app capture needs Windows 10 version 2004 or later";
        }

        if (message.Contains("did not open per-app capture", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows did not start per-app capture in time";
        }

        return exception.HResult != 0
            ? string.Create(CultureInfo.InvariantCulture, $"Windows reported 0x{exception.HResult:X8}")
            : "Windows could not open it";
    }
}
