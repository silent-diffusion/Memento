using Memento.Documents.Model.Storage;

namespace Memento.Documents.Styling;

/// <summary>
/// Saved document styles: the presets (Corporate, Minimal, Academic) plus the user's own. Changing a preset saves a
/// customised copy that <see cref="ResetBuiltInAsync"/> removes. Failures throw <see cref="DocumentStoreException"/>.
/// </summary>
public interface IStyleStore
{
    /// <summary>Presets first in their fixed order, then the user's styles by name. Unreadable files are skipped and logged.</summary>
    Task<IReadOnlyList<DocumentStyle>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The style, or <c>null</c> when there is none with that id.</summary>
    Task<DocumentStyle?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>Saves atomically and returns the style as stored.</summary>
    Task<DocumentStyle> SaveAsync(DocumentStyle style, CancellationToken cancellationToken);

    /// <summary>Copies a style under a new unique id derived from <paramref name="newName"/> (default "{name} (copy)").</summary>
    Task<DocumentStyle> DuplicateAsync(string id, string? newName, CancellationToken cancellationToken);

    /// <summary>Deletes a user style; presets cannot be deleted.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>Discards the user's changes to a preset and returns the original.</summary>
    Task<DocumentStyle> ResetBuiltInAsync(string id, CancellationToken cancellationToken);
}
