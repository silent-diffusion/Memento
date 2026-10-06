namespace Memento.Core.Host;

/// <summary>Receives the UI's lifecycle signals.</summary>
public interface IUiLifecycle
{
    /// <summary>The page has mounted, loaded its first data and fonts, and painted.</summary>
    void NotifyReady();
}
