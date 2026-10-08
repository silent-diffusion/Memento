using Memento.Core.Workers;

namespace Memento.Core.Tests.Workers;

public sealed class ProtocolLineReaderTests
{
    [Theory]
    [InlineData("a\nb\n")]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\rb")]
    [InlineData("\n\nlast")]
    [InlineData("")]
    [InlineData("one line, no ending")]
    [InlineData("mixed\r\n\r\rend\n")]
    public async Task SplitsLinesLikeTextReader(string text)
    {
        Assert.Equal(Expected(text), await ReadAllAsync(new ProtocolLineReader(new StringReader(text))));
    }

    [Fact]
    public async Task ACarriageReturnLineFeedAcrossTheBufferEdgeIsOneEnding()
    {
        // The reader fills 16 Ki characters at a time: put the \r last in the first fill and the \n first in the next.
        var text = new string('a', (16 * 1024) - 1) + "\r\nnext\n";

        var lines = await ReadAllAsync(new ProtocolLineReader(new StringReader(text)));

        Assert.Equal(Expected(text), lines);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public async Task ALineOverTheLimitIsDroppedAndTheNextOneIsRead()
    {
        var reader = new ProtocolLineReader(new StringReader("short\n" + new string('x', 50) + "\nafter\n"), maxChars: 10);

        Assert.Equal("short", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(reader.LastDiscardedLength);
        Assert.Equal(string.Empty, await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal(50, reader.LastDiscardedLength);
        Assert.Equal("after", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(reader.LastDiscardedLength);
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnOversizedLineLongerThanTheBufferIsCountedWhole()
    {
        var length = (16 * 1024 * 3) + 7;
        var reader = new ProtocolLineReader(new StringReader(new string('y', length)), maxChars: 1000);

        Assert.Equal(string.Empty, await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal(length, reader.LastDiscardedLength);
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ALineExactlyAtTheLimitIsKept()
    {
        var reader = new ProtocolLineReader(new StringReader("0123456789\n"), maxChars: 10);

        Assert.Equal("0123456789", await reader.ReadLineAsync(CancellationToken.None));
    }

    private static List<string> Expected(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static async Task<List<string>> ReadAllAsync(ProtocolLineReader reader)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
