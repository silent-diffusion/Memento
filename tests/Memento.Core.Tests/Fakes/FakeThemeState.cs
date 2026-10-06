using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

internal sealed class FakeThemeState : IThemeState
{
    public bool IsDark { get; set; }
}
