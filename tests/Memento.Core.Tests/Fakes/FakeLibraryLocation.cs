using Memento.Core.Library;

namespace Memento.Core.Tests.Fakes;

internal sealed class FakeLibraryLocation(string root) : ILibraryLocation
{
    public string Root { get; set; } = root;
}
