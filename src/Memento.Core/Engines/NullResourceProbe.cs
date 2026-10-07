namespace Memento.Core.Engines;

/// <summary>A PC with no graphics card and an idle processor (tests, non-Windows).</summary>
public sealed class NullResourceProbe : IResourceProbe
{
    public ResourceSnapshot Sample() => ResourceSnapshot.Empty;
}
