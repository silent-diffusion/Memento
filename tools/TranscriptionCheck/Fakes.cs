using Memento.Core.Host;

namespace Memento.Tools.TranscriptionCheck;

/// <summary>Host services the check does not exercise.</summary>
internal sealed class NoUi : IAppInfo, IThemeState, IExternalLauncher, IUiLifecycle, IFolderPicker
{
    public string Version => "check";

    public string OsVersion => Environment.OSVersion.VersionString;

    public bool IsDark => false;

    public bool TryOpen(Uri uri) => false;

    public void NotifyReady()
    {
    }

    public Task<string?> PickAsync(string title, string? initialPath, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
