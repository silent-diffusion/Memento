using Memento.Generation.Ai;

namespace Memento.Generation.Tests.Units;

/// <summary>The end-to-end override of a cloud endpoint only ever points at this PC.</summary>
public sealed class TestEndpointsTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8123", "http://127.0.0.1:8123/")]
    [InlineData("http://localhost:8123/fake/", "http://localhost:8123/fake/")]
    [InlineData("http://[::1]:9000", "http://[::1]:9000/")]
    public void ALoopbackAddressIsUsed(string value, string expected) => Assert.Equal(expected, TestEndpoints.Parse(value)!.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://api.example.com/")]
    [InlineData("http://192.168.1.20:8123/")]
    [InlineData("file:///C:/fake")]
    [InlineData("not a url")]
    public void AnythingElseIsIgnored(string? value) => Assert.Null(TestEndpoints.Parse(value));
}
