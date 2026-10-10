namespace Memento.Core.Maintenance;

/// <summary>
/// Whether a copy of Memento was installed by its installer (Velopack): the program runs from
/// <c>&lt;install&gt;\current\</c> and Velopack's <c>Update.exe</c> sits in <c>&lt;install&gt;</c>, normally
/// <c>%LOCALAPPDATA%\MementoApp</c> (ARCHITECTURE.md §1). The startup entry then names Velopack's launcher
/// <c>&lt;install&gt;\Memento.exe</c>, which survives updates (the <c>current</c> folder is replaced by each one); when
/// the launcher is missing it names the program in <c>current</c>, whose path also stays the same across updates.
/// </summary>
public static class InstalledLayout
{
    public const string CurrentFolder = "current";
    public const string UpdaterName = "Update.exe";
    public const string ProgramName = "Memento.exe";

    /// <summary>The program the startup entry should start, or <c>null</c> when this copy is not installed.</summary>
    /// <param name="baseDirectory">The running program's folder (<see cref="AppContext.BaseDirectory"/>).</param>
    /// <param name="fileExists">Tests replace it.</param>
    public static string? StartupProgram(string baseDirectory, Func<string, bool>? fileExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        fileExists ??= File.Exists;
        var folder = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(folder), CurrentFolder, StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(folder) is not { Length: > 0 } install
            || !fileExists(Path.Combine(install, UpdaterName)))
        {
            return null;
        }

        var launcher = Path.Combine(install, ProgramName);
        return fileExists(launcher) ? launcher : Path.Combine(folder, ProgramName);
    }
}
