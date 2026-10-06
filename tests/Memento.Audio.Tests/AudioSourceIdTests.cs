namespace Memento.Audio.Tests;

public sealed class AudioSourceIdTests
{
    [Theory]
    [InlineData("mic:{0.0.1.00000000}.{3a8b5a2c-0000-4000-8000-000000000001}", AudioSourceKind.Microphone)]
    [InlineData("system:{0.0.0.00000000}.{3a8b5a2c-0000-4000-8000-000000000002}", AudioSourceKind.System)]
    [InlineData("app:4242", AudioSourceKind.Application)]
    public void RoundTripsTheBridgeForm(string text, AudioSourceKind kind)
    {
        var id = AudioSourceId.Parse(text);

        Assert.Equal(kind, id.Kind);
        Assert.Equal(text, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mic:")]
    [InlineData("app:0")]
    [InlineData("app:-3")]
    [InlineData("app:12x")]
    [InlineData("camera:1")]
    public void RejectsMalformedIds(string? text)
    {
        Assert.False(AudioSourceId.TryParse(text, out _));
    }

    [Fact]
    public void ApplicationIdCarriesThePid()
    {
        var id = AudioSourceId.Parse("app:4242");

        Assert.Equal(4242, id.ProcessId);
        Assert.Null(id.EndpointId);
        Assert.Equal(AudioSourceId.Application(4242), id);
    }
}
