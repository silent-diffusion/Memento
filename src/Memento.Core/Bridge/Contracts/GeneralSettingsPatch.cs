namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › General for <c>settings.set</c>. Each omitted or <c>null</c> field keeps its value.</summary>
public sealed record GeneralSettingsPatch
{
    /// <summary>Also adds or removes the Windows startup entry, as <c>app.setStartup</c> does.</summary>
    public bool? StartWithWindows { get; init; }

    public bool? KeepRunningInTray { get; init; }

    public string? Language { get; init; }

    public bool? AutoUpdate { get; init; }

    /// <summary>Read only (2.0): accepted so the UI can send the whole block back, and ignored.</summary>
    public bool? StartWithWindowsAvailable { get; init; }

    /// <summary>Read only (2.0): accepted and ignored, as <see cref="StartWithWindowsAvailable"/>.</summary>
    public string? StartWithWindowsNote { get; init; }
}
