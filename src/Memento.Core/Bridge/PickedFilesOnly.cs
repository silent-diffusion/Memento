namespace Memento.Core.Bridge;

/// <summary>
/// The interface never names a file on disk: files are chosen in the host's own picker (or dropped, which the host records
/// itself). A path in a request is refused, so script running in the page could not make the host read, parse, copy or
/// connect to any file it likes (a local secret, or a <c>\\host\share</c> path that would send the user's NTLM hash).
/// </summary>
public static class PickedFilesOnly
{
    /// <exception cref="BridgeException"><c>bridge.invalidParams</c> when <paramref name="path"/> is set.</exception>
    public static void Require(string method, string? path)
    {
        if (path is not null)
        {
            throw new BridgeException(
                BridgeErrorCodes.InvalidParams,
                $"'{method}' does not take a path from the interface; the host shows its file picker. Nothing was read.");
        }
    }
}
