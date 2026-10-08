namespace Memento.Core.Projects;

/// <summary>
/// Turns the relative file names stored in <c>project.json</c> and <c>recording.state.json</c> (<c>tracks/mic.flac</c>,
/// <c>mix.flac</c>, <c>attachments/agenda.docx</c>) into full paths that are guaranteed to stay inside the project folder.
/// Project folders can be copied in from elsewhere, so their manifests are untrusted input: without this check a crafted
/// <c>"file": "..\\..\\Music\\x.wav"</c> or an absolute path would let optimize delete, finalize overwrite, recovery
/// patch, export copy or the AI payload read a file anywhere on the disk.
/// </summary>
public static class ProjectPaths
{
    /// <summary>
    /// True for a plain relative path of one or more names: not rooted, no drive or stream (<c>:</c>), no <c>.</c> or
    /// <c>..</c> segment, no empty segment, no name ending in a dot or space, no invalid characters.
    /// </summary>
    public static bool IsSafeRelative(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1024 || Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var segment in relative.Split('/', '\\'))
        {
            if (segment.Length == 0
                || segment.Trim('.').Length == 0
                || segment.EndsWith('.')
                || segment.EndsWith(' ')
                || segment.StartsWith(' ')
                || segment.IndexOfAny(invalid) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The full path of <paramref name="relative"/> inside <paramref name="projectFolder"/>.</summary>
    /// <exception cref="InvalidDataException">The name is not a plain relative path or resolves outside the folder.</exception>
    public static string Resolve(string projectFolder, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        if (!IsSafeRelative(relative))
        {
            throw new InvalidDataException($"The project file name '{Shorten(relative)}' is not a file inside the project folder.");
        }

        var root = Path.GetFullPath(projectFolder).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The project file name '{Shorten(relative)}' resolves outside the project folder.");
        }

        return full;
    }

    /// <summary>As <see cref="Resolve"/>, and the file must also sit inside <paramref name="subfolder"/> (e.g. <c>attachments</c>).</summary>
    /// <exception cref="InvalidDataException">The name is not inside that subfolder of the project.</exception>
    public static string ResolveIn(string projectFolder, string subfolder, string relative)
    {
        var full = Resolve(projectFolder, relative);
        var inside = Path.GetFullPath(Path.Combine(projectFolder, subfolder)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The project file name '{Shorten(relative)}' is not inside the project's {subfolder} folder.");
        }

        return full;
    }

    /// <summary>
    /// The first file name in <paramref name="manifest"/> that is not a plain relative path, as <c>field: value</c>, or
    /// null when every name is safe. Read on load, so a crafted manifest never reaches a stage.
    /// </summary>
    public static string? FirstUnsafe(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        for (var i = 0; i < manifest.Tracks.Count; i++)
        {
            var track = manifest.Tracks[i];
            if (!IsSafeRelative(track.File))
            {
                return $"tracks[{i}].file";
            }

            if (track.CaptureFile is not null && !IsSafeRelative(track.CaptureFile))
            {
                return $"tracks[{i}].captureFile";
            }
        }

        if (manifest.Mix is { } mix && !IsSafeRelative(mix.File))
        {
            return "mix.file";
        }

        if (manifest.Peaks is not null && !IsSafeRelative(manifest.Peaks))
        {
            return "peaks";
        }

        for (var i = 0; i < manifest.Attachments.Count; i++)
        {
            // Attachments are kept in attachments/; AttachmentService only touches files there (ResolveIn).
            if (!IsSafeRelative(manifest.Attachments[i].File))
            {
                return $"attachments[{i}].file";
            }
        }

        return null;
    }

    /// <summary>The first track file in <paramref name="state"/> that is not a plain relative path, or null.</summary>
    public static string? FirstUnsafe(RecordingStateDocument state)
    {
        ArgumentNullException.ThrowIfNull(state);
        for (var i = 0; i < state.Tracks.Count; i++)
        {
            if (!IsSafeRelative(state.Tracks[i].File))
            {
                return $"tracks[{i}].file";
            }
        }

        return null;
    }

    private static string Shorten(string? value) =>
        value is null ? string.Empty : value.Length <= 80 ? value : value[..80] + "…";
}
