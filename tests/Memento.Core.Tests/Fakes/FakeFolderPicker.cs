using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

internal sealed class FakeFolderPicker : IFolderPicker
{
    public string? Answer { get; set; }

    public List<(string Title, string? InitialPath)> Calls { get; } = [];

    public Task<string?> PickAsync(string title, string? initialPath, CancellationToken cancellationToken)
    {
        Calls.Add((title, initialPath));
        return Task.FromResult(Answer);
    }
}
