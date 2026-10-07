namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>app.setStartup</c>.</summary>
public sealed record AppSetStartupParams
{
    public required bool StartWithWindows { get; init; }
}
