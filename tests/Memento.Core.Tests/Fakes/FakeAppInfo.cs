using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

internal sealed class FakeAppInfo : IAppInfo
{
    public string Version { get; init; } = "0.1.0";

    public string OsVersion { get; init; } = "Windows 10.0.26200";
}
