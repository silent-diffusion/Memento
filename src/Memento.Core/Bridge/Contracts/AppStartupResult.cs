namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>app.setStartup</c>: whether Memento now starts with Windows.</summary>
public sealed record AppStartupResult(bool StartWithWindows);
