using System.Globalization;
using Memento.Core.Projects;

namespace Memento.Core.Recording;

/// <summary>
/// Which stopped recordings "Resume recording" (2.1) may add a segment to: one that was recorded (not imported) and
/// started on the current day (local time), saved (ready or recovered), with its separate track files kept. A recording
/// from an earlier day is left as it is: a new recording is the clearer record of another day. The answer is the
/// §17 sentence that says why not, or <c>null</c> when it may be resumed.
/// </summary>
public static class ResumePolicy
{
    public static string? WhyNot(ProjectManifest manifest, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var title = manifest.Details.Title;
        if (manifest.ImportedFrom is not null)
        {
            return $"\"{title}\" was imported from a file, so there is no recording to resume. Start a new recording instead; the imported one is unchanged.";
        }

        if (manifest.State is ProjectStates.Recording or ProjectStates.Finalizing)
        {
            return $"\"{title}\" is still being recorded or saved. Resume it once it shows as saved in the Library; nothing is lost.";
        }

        if (manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
        {
            return $"\"{title}\" was not saved completely, so Memento does not add to it. Its files are kept; Memento tries to save it again at the next start. Start a new recording instead.";
        }

        if (manifest.CreatedAt.ToOffset(now.Offset).Date != now.Date)
        {
            var day = manifest.CreatedAt.ToOffset(now.Offset).ToString("d MMMM", CultureInfo.CurrentCulture);
            return $"\"{title}\" was recorded on {day}; only recordings started today can be resumed. Start a new recording instead; this one is unchanged.";
        }

        if (manifest.MixOnly is not null)
        {
            return $"\"{title}\" keeps only its mix (Keep only the mix removed the separate tracks), so a new segment could not be mixed with it. Start a new recording instead; this one is unchanged.";
        }

        if (manifest.AudioRemoved is not null)
        {
            return $"\"{title}\" had its audio deleted, so a new segment could not be joined to it. Start a new recording instead; its transcript and documents are unchanged.";
        }

        if (manifest.Tracks.Count == 0)
        {
            return $"\"{title}\" has no recorded tracks to continue. Start a new recording instead.";
        }

        return null;
    }
}
