namespace Memento.Core.Host;

/// <summary>Whether Memento starts when the user signs in to Windows (Settings › General › Start with Windows).</summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }

    /// <summary>
    /// Whether this copy of Memento may register itself: only an installed copy does (2.0), so a copy run from a build
    /// folder or a download never leaves a startup entry that points at a file that may move.
    /// </summary>
    bool IsAvailable => true;

    /// <summary>Why <see cref="IsAvailable"/> is false, as a sentence for Settings; <c>null</c> when it is available.</summary>
    string? UnavailableReason => null;

    /// <summary>Adds or removes the entry.</summary>
    /// <exception cref="UnauthorizedAccessException">Windows refused the change.</exception>
    /// <exception cref="IOException">Windows refused the change.</exception>
    /// <exception cref="InvalidOperationException">Adding it while <see cref="IsAvailable"/> is false; nothing was changed.</exception>
    void SetEnabled(bool enabled);
}
