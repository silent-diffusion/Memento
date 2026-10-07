using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Documents.Model.Storage;
using Memento.Documents.Render;
using Memento.Documents.Styling;
using Memento.Documents.Templates;
using Memento.Generation.Bridge;

namespace Memento.Generation.Documents;

/// <summary>
/// The <c>styles.*</c> methods over Memento.Documents' style store (<c>%LOCALAPPDATA%\Memento\styles</c> over the
/// presets): saving, copies, deleting (refused for presets and for a template's default style), Reset, and the Style
/// editor's live sample page. <c>styles.changed</c> after every write.
/// </summary>
public sealed class StyleService(IStyleStore styles, ITemplateStore templates, DocumentHtmlRenderer renderer, M4EventPublisher events, TimeProvider time)
{
    public async Task<IReadOnlyList<Style>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await styles.ListAsync(cancellationToken);
        var usage = await UsageAsync(cancellationToken);
        return all.Select(s => M4Mapping.ToBridge(s, usage.GetValueOrDefault(s.Id)?.Count ?? 0)).ToList();
    }

    public async Task<DocumentStyle> FindAsync(string styleId, CancellationToken cancellationToken)
    {
        DocumentStyle? style;
        try
        {
            style = await styles.GetAsync(styleId, cancellationToken);
        }
        catch (DocumentStoreException ex)
        {
            throw M4Errors.Invalid(ex.Message, styleId);
        }

        return style ?? throw M4Errors.StyleNotFound(styleId);
    }

    /// <summary>The style, or Corporate when the id names none (a document whose style was deleted still opens).</summary>
    public async Task<DocumentStyle> FindOrDefaultAsync(string? styleId, CancellationToken cancellationToken) =>
        (styleId is null ? null : await styles.GetAsync(styleId, cancellationToken)) ?? BuiltInStyles.Corporate;

    public async Task<Style> GetAsync(string styleId, CancellationToken cancellationToken)
    {
        var style = await FindAsync(styleId, cancellationToken);
        var usage = await UsageAsync(cancellationToken);
        return M4Mapping.ToBridge(style, usage.GetValueOrDefault(style.Id)?.Count ?? 0);
    }

    public async Task<Style> SaveAsync(Style style, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (string.IsNullOrWhiteSpace(style.Name))
        {
            throw M4Errors.Invalid("A style needs a name.", "name");
        }

        var existing = string.IsNullOrWhiteSpace(style.Id) ? null : await styles.GetAsync(style.Id, cancellationToken);
        var stored = M4Mapping.FromSettings(style.Settings ?? throw M4Errors.Invalid("A style needs its settings.", "settings"), existing ?? new DocumentStyle()) with { Name = style.Name.Trim() };

        // Presets are never saved over; saving one creates a copy (BRIDGE.md M4).
        if (existing is { BuiltIn: true } && string.Equals(existing.Name, stored.Name, StringComparison.Ordinal))
        {
            stored = stored with { Name = stored.Name + " (copy)" };
        }

        if (existing is null || existing.BuiltIn)
        {
            var taken = (await styles.ListAsync(cancellationToken)).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var id = existing is null && StoreIds.IsValid(style.Id) && !taken.Contains(style.Id) ? style.Id : StoreIds.Unique(StoreIds.Slug(stored.Name), taken.Contains);
            stored = stored with { Id = id, BuiltIn = false };
        }

        DocumentStyle saved;
        try
        {
            saved = await styles.SaveAsync(stored with { ModifiedAt = time.GetLocalNow() }, cancellationToken);
        }
        catch (DocumentStoreException ex)
        {
            throw M4Errors.Invalid(ex.Message, stored.Id);
        }

        events.PublishStylesChanged();
        var usage = await UsageAsync(cancellationToken);
        return M4Mapping.ToBridge(saved, usage.GetValueOrDefault(saved.Id)?.Count ?? 0);
    }

    public async Task<Style> DuplicateAsync(string styleId, CancellationToken cancellationToken)
    {
        await FindAsync(styleId, cancellationToken);
        var copy = await styles.DuplicateAsync(styleId, null, cancellationToken);
        copy = await styles.SaveAsync(copy with { ModifiedAt = time.GetLocalNow() }, cancellationToken);
        events.PublishStylesChanged();
        return M4Mapping.ToBridge(copy, 0);
    }

    public async Task DeleteAsync(string styleId, CancellationToken cancellationToken)
    {
        var style = await FindAsync(styleId, cancellationToken);
        if (style.BuiltIn)
        {
            throw M4Errors.StyleBuiltIn(style.Name);
        }

        if ((await UsageAsync(cancellationToken)).GetValueOrDefault(styleId) is { Count: > 0 } users)
        {
            throw M4Errors.StyleInUse(style.Name, users);
        }

        await styles.DeleteAsync(styleId, cancellationToken);
        events.PublishStylesChanged();
    }

    public async Task<Style> ResetBuiltInAsync(string styleId, CancellationToken cancellationToken)
    {
        var style = await FindAsync(styleId, cancellationToken);
        if (!style.BuiltIn)
        {
            throw M4Errors.Invalid($"\"{style.Name}\" is not a preset, so there is nothing to reset it to. Nothing was changed.", styleId);
        }

        var reset = await styles.ResetBuiltInAsync(styleId, cancellationToken);
        events.PublishStylesChanged();
        var usage = await UsageAsync(cancellationToken);
        return M4Mapping.ToBridge(reset, usage.GetValueOrDefault(reset.Id)?.Count ?? 0);
    }

    /// <summary>The Style editor's sample minutes in the settings as they are now (nothing is saved).</summary>
    public HtmlResult SampleHtml(StyleSettings settings)
    {
        var style = M4Mapping.FromSettings(settings, BuiltInStyles.Corporate with { Id = "sample", Name = "Sample" });
        var paper = renderer.RenderSample(style);
        return new HtmlResult(paper.Html);
    }

    /// <summary>Style id → the names of the templates that use it as their default.</summary>
    private async Task<Dictionary<string, List<string>>> UsageAsync(CancellationToken cancellationToken) =>
        (await templates.ListAsync(cancellationToken))
            .GroupBy(t => t.DefaultStyleId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Name).ToList(), StringComparer.Ordinal);
}
