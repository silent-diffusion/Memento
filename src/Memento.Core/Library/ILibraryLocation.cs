namespace Memento.Core.Library;

/// <summary>The library folder in effect (Settings › Storage), read each time it is needed.</summary>
public interface ILibraryLocation
{
    /// <summary>Full path of the library folder, e.g. <c>%LOCALAPPDATA%\Memento\Library</c>.</summary>
    string Root { get; }
}
