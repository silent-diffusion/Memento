namespace Memento.Core.Host;

/// <summary>The picker when the host has none (tests, tools): always cancelled.</summary>
public sealed class UnavailableFilePicker : IFilePicker
{
    public Task<string?> PickFileAsync(string title, IReadOnlyList<FileFilter> filters, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
