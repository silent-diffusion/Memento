namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>settings.set</c>: a partial update. Omitted (or <c>null</c>) fields keep their value.
/// </summary>
public sealed record SettingsSetParams
{
    /// <summary><c>"system"</c>, <c>"light"</c> or <c>"dark"</c>.</summary>
    public string? Theme { get; init; }

    /// <summary>Accepted only when it equals the current location; moving the library is a separate flow.</summary>
    public string? LibraryPath { get; init; }

    /// <summary><c>"comfortable"</c> or <c>"compact"</c>.</summary>
    public string? ListDensity { get; init; }
}
