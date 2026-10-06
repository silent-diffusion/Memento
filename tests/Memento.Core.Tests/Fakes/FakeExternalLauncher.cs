using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

internal sealed class FakeExternalLauncher : IExternalLauncher
{
    public List<Uri> Opened { get; } = [];

    public bool Succeeds { get; set; } = true;

    public bool TryOpen(Uri uri)
    {
        if (Succeeds)
        {
            Opened.Add(uri);
        }

        return Succeeds;
    }
}
