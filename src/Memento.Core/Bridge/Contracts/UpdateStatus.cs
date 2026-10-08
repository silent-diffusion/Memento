namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Result of <c>updates.status</c>, <c>updates.check</c> and payload of <c>updates.progress</c> (BRIDGE.md "Updates").
/// </summary>
/// <param name="CurrentVersion">The running version.</param>
/// <param name="State"><c>unavailable</c> (this copy cannot update itself), <c>idle</c>, <c>checking</c>,
/// <c>downloading</c>, <c>ready</c> (downloaded; restart to update) or <c>failed</c> (the last check by hand failed).</param>
/// <param name="AvailableVersion">The newer version found, while downloading or ready.</param>
/// <param name="Percent">Download progress while <c>downloading</c>.</param>
/// <param name="LastCheckedAt">When the feed last answered, or <c>null</c>.</param>
/// <param name="Message">What happened, in words, after a check by hand (none found, or why it failed); else <c>null</c>.</param>
/// <param name="Deferred">A newer version waits to download until no recording or processing is running.</param>
public sealed record UpdateStatus(
    string CurrentVersion,
    string State,
    string? AvailableVersion,
    int? Percent,
    DateTimeOffset? LastCheckedAt,
    string? Message,
    bool Deferred);
