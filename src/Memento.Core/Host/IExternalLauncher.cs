namespace Memento.Core.Host;

/// <summary>Opens a URI with the user's default handler (browser, Windows Settings).</summary>
public interface IExternalLauncher
{
    /// <summary>Returns <c>false</c> when Windows could not start a handler for <paramref name="uri"/>.</summary>
    bool TryOpen(Uri uri);
}
