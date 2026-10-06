namespace Memento.Core.Host;

/// <summary>The effective theme: Windows app theme, overridden by Settings or the command line.</summary>
public interface IThemeState
{
    bool IsDark { get; }
}
