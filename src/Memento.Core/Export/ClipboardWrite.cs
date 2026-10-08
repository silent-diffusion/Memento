using Memento.Core.Bridge;
using Memento.Core.Host;

namespace Memento.Core.Export;

/// <summary>Writes the clipboard for a bridge method and turns a refusal into the §17 <c>clipboard.unavailable</c> answer.</summary>
public static class ClipboardWrite
{
    /// <param name="what">What was being copied, for the message: "the transcript", "“Minutes”".</param>
    public static async Task SetAsync(IClipboard clipboard, ClipboardContent content, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        try
        {
            await clipboard.SetAsync(content, cancellationToken);
        }
        catch (ClipboardUnavailableException ex)
        {
            throw new BridgeException(
                DomainErrorCodes.ClipboardUnavailable,
                $"Windows did not let Memento use the clipboard, so {what} was not copied. Nothing was changed. Try again in a moment; if another program keeps the clipboard open, close it first.",
                ex.Message);
        }
    }
}
