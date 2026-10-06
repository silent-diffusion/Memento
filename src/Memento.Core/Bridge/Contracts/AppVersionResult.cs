namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>app.version</c>.</summary>
public sealed record AppVersionResult(string Version, string OsVersion, bool IsDarkTheme);
