namespace Memento.Core.Export;

/// <summary>Where <see cref="ExportJournal"/> keeps its file: <c>%LOCALAPPDATA%\Memento\exports-running.json</c>.</summary>
public sealed record ExportJournalOptions(string Path)
{
    public static ExportJournalOptions Default => new(System.IO.Path.Combine(AppPaths.DataRoot, "exports-running.json"));
}
