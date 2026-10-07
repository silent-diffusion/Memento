using Memento.Documents.Model.Storage;
using Microsoft.Extensions.Logging;

namespace Memento.Documents.Templates;

/// <summary>
/// <see cref="ITemplateStore"/> over a folder of <c>&lt;id&gt;.json</c> files (the global
/// <c>%LOCALAPPDATA%\Memento\templates</c> or a project's <c>templates/</c>), layered over <see cref="BuiltInTemplates"/>.
/// </summary>
public sealed class FileTemplateStore : ITemplateStore
{
    private readonly JsonItemStore<DocumentTemplate> _store;

    public FileTemplateStore(string folder, ILogger<FileTemplateStore>? logger = null)
    {
        Folder = folder;
        _store = new JsonItemStore<DocumentTemplate>(
            folder,
            "template",
            TemplateJsonContext.Default.DocumentTemplate,
            BuiltInTemplates.All,
            new StoreItemAccessors<DocumentTemplate>(
                t => t.Id,
                t => t.Name,
                t => t.SchemaVersion,
                DocumentTemplate.CurrentSchemaVersion,
                (t, id, name) => t with { Id = id, Name = name },
                (t, builtIn, customized) => t with { BuiltIn = builtIn, Customized = customized, SchemaVersion = DocumentTemplate.CurrentSchemaVersion }),
            logger);
    }

    public string Folder { get; }

    public Task<IReadOnlyList<DocumentTemplate>> ListAsync(CancellationToken cancellationToken) => _store.ListAsync(cancellationToken);

    public Task<DocumentTemplate?> GetAsync(string id, CancellationToken cancellationToken) => _store.GetAsync(id, cancellationToken);

    public Task<DocumentTemplate> SaveAsync(DocumentTemplate documentTemplate, CancellationToken cancellationToken) => _store.SaveAsync(documentTemplate, cancellationToken);

    public Task<DocumentTemplate> DuplicateAsync(string id, string? newName, CancellationToken cancellationToken) =>
        _store.DuplicateAsync(id, newName, cancellationToken);

    public Task DeleteAsync(string id, CancellationToken cancellationToken) => _store.DeleteAsync(id, cancellationToken);

    public Task<DocumentTemplate> ResetBuiltInAsync(string id, CancellationToken cancellationToken) => _store.ResetBuiltInAsync(id, cancellationToken);
}
