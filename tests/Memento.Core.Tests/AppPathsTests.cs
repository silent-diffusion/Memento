namespace Memento.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void EveryPathLivesUnderTheLocalAppDataRoot()
    {
        var root = AppPaths.DataRoot;

        Assert.Equal(AppPaths.AppFolderName, Path.GetFileName(root));
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.Equal(Path.Combine(root, "Library"), AppPaths.DefaultLibrary);
        Assert.Equal(Path.Combine(root, "logs"), AppPaths.Logs);
        Assert.Equal(Path.Combine(root, "models"), AppPaths.Models);
        Assert.Equal(Path.Combine(root, "webview2"), AppPaths.WebView2UserData);
        Assert.Equal(Path.Combine(root, "settings.json"), AppPaths.SettingsFile);
    }

    [Fact]
    public void TheDefaultLibraryIsNotInDocuments()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Assert.False(
            !string.IsNullOrEmpty(documents) && AppPaths.DefaultLibrary.StartsWith(documents, StringComparison.OrdinalIgnoreCase),
            "The library must not default to Documents, which OneDrive often syncs.");
    }
}
