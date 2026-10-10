using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Host;
using Memento.Generation.Bridge;
using BridgeTemplate = Memento.Core.Bridge.Contracts.Template;

namespace Memento.Generation.Generation;

/// <summary>
/// <c>generation.copyPreview</c> (2.0): "Preview what will be sent", or one of its requests, on the Windows clipboard as
/// text through the host (the page never uses the browser's clipboard). The preview is put together again, so the copy
/// is exactly what the sheet shows for the template as it is now. Nothing is sent.
/// </summary>
public sealed class PreviewClipboard(GenerationService generation, IClipboard clipboard)
{
    /// <exception cref="Core.Bridge.BridgeException"><c>bridge.invalidParams</c> (detail <c>passId</c>) for a pass the preview does not have; <c>clipboard.unavailable</c>.</exception>
    public async Task<PreviewCopyResult> CopyAsync(string recordingId, BridgeTemplate template, string? passId, CancellationToken cancellationToken)
    {
        var preview = await generation.PreviewAsync(recordingId, template, cancellationToken);
        if (passId is not null && preview.Passes.All(p => p.Id != passId))
        {
            throw M4Errors.Invalid($"The preview has no request '{passId}'. Nothing was copied.", "passId");
        }

        var text = PayloadPreview.Text(preview, passId);
        await ClipboardWrite.SetAsync(clipboard, new ClipboardContent(text, null), passId is null ? "the preview" : "the request", cancellationToken);
        return new PreviewCopyResult(text.Length, passId is null ? preview.Passes.Count : 1);
    }
}
