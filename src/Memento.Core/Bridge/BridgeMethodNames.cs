namespace Memento.Core.Bridge;

/// <summary>Request method names (<c>area.verb</c>). Mirrored in <c>ui/src/bridge/types.ts</c>.</summary>
public static class BridgeMethodNames
{
    public const string AppVersion = "app.version";
    public const string AppOpenExternal = "app.openExternal";
    public const string SettingsGet = "settings.get";
    public const string SettingsSet = "settings.set";
    public const string LibraryList = "library.list";
    public const string UiReady = "ui.ready";
}
