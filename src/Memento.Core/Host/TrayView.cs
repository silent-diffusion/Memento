using System.Globalization;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;

namespace Memento.Core.Host;

/// <summary>
/// What the notification-area icon says (Settings › General › Keep running in the tray, 2.0): its tooltip, a status line
/// at the top of its menu while a recording is in progress, and the labels of Open, Record and Quit. Worded by the
/// DESIGN.md §17 rules: name the thing, give the time, say what is safe.
/// </summary>
/// <param name="Tooltip">At most 127 characters (the Windows limit).</param>
/// <param name="StatusLine">A recording in progress, else <c>null</c> (no status line).</param>
/// <param name="IsRecording">Recording or paused: the icon shows the recording badge.</param>
public sealed record TrayView(string Tooltip, string? StatusLine, string OpenLabel, string RecordLabel, string QuitLabel, bool IsRecording)
{
    public const int MaxTooltipLength = 127;

    /// <summary>Shown once per run, the first time closing the window leaves Memento in the tray.</summary>
    public const string StillRunningTitle = "Memento is still running";

    public const string StillRunningBody = "It keeps recording and processing here. Open it or quit from this icon.";

    public const string StillRecordingBody = "The recording continues and is saved as it records. Open Memento from this icon to stop it.";

    /// <summary>The view for <paramref name="current"/> (<c>recording.current</c>; <c>null</c> when nothing records).</summary>
    public static TrayView For(RecordingStatePayload? current, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (current is null || current.State is not ("recording" or "paused" or "finalizing"))
        {
            return new TrayView("Memento", null, "Open Memento", "New recording", "Quit Memento", false);
        }

        var clock = HumanFormat.Clock(current.ElapsedMs);
        var started = current.StartedAt.ToLocalTime().ToString("t", culture);
        var tracks = HumanFormat.Count(current.Tracks.Count, "audio track", "audio tracks");
        var (tooltip, status) = current.State switch
        {
            "paused" => (
                $"Memento · recording paused at {clock} · everything so far is saved",
                $"Paused at {clock} · started {started}"),
            "finalizing" => (
                $"Memento · saving the recording ({clock}) · it opens in Review when done",
                $"Saving the recording · {clock}"),
            _ => (
                $"Memento · recording {clock} · {tracks} · saved as it records",
                $"Recording {clock} · started {started} · {tracks}"),
        };
        var recording = current.State is "recording" or "paused";
        return new TrayView(
            Fit(tooltip),
            status,
            "Open Memento",
            recording ? "Show the recording" : "New recording",
            recording ? "Quit Memento (stops the recording; everything so far is kept)" : "Quit Memento",
            recording);
    }

    private static string Fit(string text) => text.Length <= MaxTooltipLength ? text : string.Concat(text.AsSpan(0, MaxTooltipLength - 1), "…");
}
