namespace Memento.Core.Models;

/// <summary>A catalog model and what is on disk for it now.</summary>
/// <param name="Installing">Bytes downloaded so far while it installs; otherwise <c>null</c>.</param>
public sealed record ModelState(ModelCatalogEntry Entry, bool Installed, long? Installing);
