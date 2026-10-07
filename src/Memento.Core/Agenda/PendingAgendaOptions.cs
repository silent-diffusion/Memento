namespace Memento.Core.Agendas;

/// <summary>Where imported agenda files wait for <c>agenda.apply</c>, and for how long.</summary>
/// <param name="Folder">A folder only this process uses; it is removed when Memento closes.</param>
public sealed record PendingAgendaOptions(string Folder, TimeSpan Expiry)
{
    /// <summary>An hour: long enough to review and fix the items, short enough not to keep copies around.</summary>
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(1);

    /// <summary>The folder in %TEMP%\Memento that holds one <c>&lt;pid&gt;</c> folder per running Memento.</summary>
    public const string PendingFolderName = "agenda-pending";

    public static PendingAgendaOptions Default =>
        new(Path.Combine(Path.GetTempPath(), "Memento", PendingFolderName, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)), DefaultExpiry);
}
