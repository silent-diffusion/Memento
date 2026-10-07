using Memento.Documents.Model.Storage;
using Microsoft.Extensions.Logging;

namespace Memento.Documents.Styling;

/// <summary><see cref="IStyleStore"/> over a folder of <c>&lt;id&gt;.json</c> files, layered over <see cref="BuiltInStyles"/>.</summary>
public sealed class FileStyleStore : IStyleStore
{
    private readonly JsonItemStore<DocumentStyle> _store;

    public FileStyleStore(string folder, ILogger<FileStyleStore>? logger = null)
    {
        Folder = folder;
        _store = new JsonItemStore<DocumentStyle>(
            folder,
            "style",
            StyleJsonContext.Default.DocumentStyle,
            BuiltInStyles.All,
            new StoreItemAccessors<DocumentStyle>(
                s => s.Id,
                s => s.Name,
                s => s.SchemaVersion,
                DocumentStyle.CurrentSchemaVersion,
                (s, id, name) => s with { Id = id, Name = name },
                (s, builtIn, customized) => s with { BuiltIn = builtIn, Customized = customized, SchemaVersion = DocumentStyle.CurrentSchemaVersion }),
            logger);
    }

    public string Folder { get; }

    public Task<IReadOnlyList<DocumentStyle>> ListAsync(CancellationToken cancellationToken) => _store.ListAsync(cancellationToken);

    public Task<DocumentStyle?> GetAsync(string id, CancellationToken cancellationToken) => _store.GetAsync(id, cancellationToken);

    public Task<DocumentStyle> SaveAsync(DocumentStyle style, CancellationToken cancellationToken) => _store.SaveAsync(style, cancellationToken);

    public Task<DocumentStyle> DuplicateAsync(string id, string? newName, CancellationToken cancellationToken) =>
        _store.DuplicateAsync(id, newName, cancellationToken);

    public Task DeleteAsync(string id, CancellationToken cancellationToken) => _store.DeleteAsync(id, cancellationToken);

    public Task<DocumentStyle> ResetBuiltInAsync(string id, CancellationToken cancellationToken) => _store.ResetBuiltInAsync(id, cancellationToken);
}
