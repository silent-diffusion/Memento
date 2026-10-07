namespace Memento.Core.Host;

/// <summary>Whether Memento starts when the user signs in to Windows (Settings › General › Start with Windows).</summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }

    /// <summary>Adds or removes the entry.</summary>
    /// <exception cref="UnauthorizedAccessException">Windows refused the change.</exception>
    /// <exception cref="IOException">Windows refused the change.</exception>
    void SetEnabled(bool enabled);
}
