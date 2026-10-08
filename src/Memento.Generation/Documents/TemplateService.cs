using Memento.Core.Bridge;
using Memento.Documents.Model.Modules;
using Memento.Documents.Model.Storage;
using Memento.Documents.Styling;
using Memento.Documents.Templates;
using Memento.Generation.Bridge;
using BridgeTemplate = Memento.Core.Bridge.Contracts.Template;

namespace Memento.Generation.Documents;

/// <summary>
/// The <c>templates.*</c> methods over Memento.Documents' template store (<c>%LOCALAPPDATA%\Memento\templates</c> layered
/// over the built-ins): a new id for a new template, a copy when a built-in is saved under a new name, the style checked,
/// and <c>templates.changed</c> after every write.
/// </summary>
public sealed class TemplateService(ITemplateStore templates, IStyleStore styles, ModuleCatalog catalog, M4EventPublisher events, TimeProvider time)
{
    public async Task<IReadOnlyList<BridgeTemplate>> ListAsync(CancellationToken cancellationToken) =>
        (await templates.ListAsync(cancellationToken)).Select(M4Mapping.ToBridge).ToList();

    public async Task<DocumentTemplate> FindAsync(string templateId, CancellationToken cancellationToken)
    {
        DocumentTemplate? template;
        try
        {
            template = await templates.GetAsync(templateId, cancellationToken);
        }
        catch (DocumentStoreException ex)
        {
            throw M4Errors.Invalid(ex.Message, templateId);
        }

        return template ?? throw M4Errors.TemplateNotFound(templateId);
    }

    public async Task<BridgeTemplate> GetAsync(string templateId, CancellationToken cancellationToken) =>
        M4Mapping.ToBridge(await FindAsync(templateId, cancellationToken));

    /// <summary>
    /// Saves: an empty or unknown id makes a new template; a built-in is never saved over, saving one creates a copy
    /// (BRIDGE.md M4); any other template is replaced.
    /// </summary>
    public async Task<BridgeTemplate> SaveAsync(BridgeTemplate template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        var existing = string.IsNullOrWhiteSpace(template.Id) ? null : await templates.GetAsync(template.Id, cancellationToken);
        var stored = M4Mapping.FromBridge(template, existing, catalog);
        if (await styles.GetAsync(stored.DefaultStyleId, cancellationToken) is null)
        {
            throw M4Errors.StyleNotFound(stored.DefaultStyleId);
        }

        // BRIDGE.md M4: built-ins are never saved over; saving one creates a copy. A new template never takes the name of
        // one that is already in the library ("Meeting minutes (copy)", then "Meeting minutes (copy 2)"), so the
        // Builder's template list tells them apart.
        var asNew = existing is null || existing.BuiltIn;
        if (asNew)
        {
            var all = await templates.ListAsync(cancellationToken);
            stored = stored with { Name = UniqueName(stored.Name, all.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)) };
            var taken = all.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            var id = string.IsNullOrWhiteSpace(template.Id) || existing is not null ? StoreIds.Unique(StoreIds.Slug(stored.Name), taken.Contains) : template.Id;
            if (!StoreIds.IsValid(id))
            {
                id = StoreIds.Unique(StoreIds.Slug(stored.Name), taken.Contains);
            }

            stored = stored with { Id = id, BuiltIn = false };
        }

        var saved = await SaveStoredAsync(stored with { ModifiedAt = time.GetLocalNow() }, cancellationToken);
        return M4Mapping.ToBridge(saved);
    }

    /// <summary><paramref name="name"/> when no template has it, else "name (copy)", "name (copy 2)", …</summary>
    internal static string UniqueName(string name, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return name;
        }

        var copy = name + " (copy)";
        for (var n = 2; taken.Contains(copy); n++)
        {
            copy = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} (copy {n})");
        }

        return copy;
    }

    public async Task<DocumentTemplate> SaveStoredAsync(DocumentTemplate template, CancellationToken cancellationToken)
    {
        DocumentTemplate saved;
        try
        {
            saved = await templates.SaveAsync(template, cancellationToken);
        }
        catch (DocumentStoreException ex)
        {
            throw M4Errors.Invalid(ex.Message, template.Id);
        }

        events.PublishTemplatesChanged();
        return saved;
    }

    public async Task<BridgeTemplate> DuplicateAsync(string templateId, CancellationToken cancellationToken)
    {
        await FindAsync(templateId, cancellationToken);
        var copy = await templates.DuplicateAsync(templateId, null, cancellationToken);
        copy = await templates.SaveAsync(copy with { ModifiedAt = time.GetLocalNow() }, cancellationToken);
        events.PublishTemplatesChanged();
        return M4Mapping.ToBridge(copy);
    }

    public async Task DeleteAsync(string templateId, CancellationToken cancellationToken)
    {
        var template = await FindAsync(templateId, cancellationToken);
        if (template.BuiltIn)
        {
            throw M4Errors.TemplateBuiltIn(template.Name);
        }

        await templates.DeleteAsync(templateId, cancellationToken);
        events.PublishTemplatesChanged();
    }

    public async Task<BridgeTemplate> ResetBuiltInAsync(string templateId, CancellationToken cancellationToken)
    {
        var template = await FindAsync(templateId, cancellationToken);
        if (!template.BuiltIn)
        {
            throw M4Errors.Invalid($"\"{template.Name}\" is not a built-in template, so there is nothing to reset it to. Nothing was changed.", templateId);
        }

        var reset = await templates.ResetBuiltInAsync(templateId, cancellationToken);
        events.PublishTemplatesChanged();
        return M4Mapping.ToBridge(reset);
    }
}
