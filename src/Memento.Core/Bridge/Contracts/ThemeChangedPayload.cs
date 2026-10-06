namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of the <c>theme.changed</c> event: the effective theme after Windows and the Settings override.</summary>
public sealed record ThemeChangedPayload(bool IsDark);
