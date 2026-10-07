using Memento.Documents.Model.Storage;

namespace Memento.Documents.Templates;

/// <summary>
/// Saved document templates: the built-ins (Meeting minutes, Interview notes, Lecture summary, Dictation clean-up) plus the
/// user's own. Changing a built-in saves a customised copy that <see cref="ResetBuiltInAsync"/> removes.
/// Failures throw <see cref="DocumentStoreException"/> with a <see cref="StoreErrorCodes"/> code.
/// </summary>
public interface ITemplateStore
{
    /// <summary>Built-ins first in their fixed order, then the user's templates by name. Unreadable files are skipped and logged.</summary>
    Task<IReadOnlyList<DocumentTemplate>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The template, or <c>null</c> when there is none with that id.</summary>
    Task<DocumentTemplate?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>Saves atomically and returns the template as stored (with <see cref="DocumentTemplate.BuiltIn"/> set).</summary>
    Task<DocumentTemplate> SaveAsync(DocumentTemplate documentTemplate, CancellationToken cancellationToken);

    /// <summary>Copies a template under a new unique id derived from <paramref name="newName"/> (default "{name} (copy)").</summary>
    Task<DocumentTemplate> DuplicateAsync(string id, string? newName, CancellationToken cancellationToken);

    /// <summary>Deletes a user template; built-ins cannot be deleted.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>Discards the user's changes to a built-in template and returns the original.</summary>
    Task<DocumentTemplate> ResetBuiltInAsync(string id, CancellationToken cancellationToken);
}
