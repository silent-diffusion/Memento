using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

internal sealed class FakeUiLifecycle : IUiLifecycle
{
    public int ReadyCount { get; private set; }

    public void NotifyReady() => ReadyCount++;
}
