using Memento.Core.Workers;

namespace Memento.Core.Tests.Workers;

/// <summary>The worker's stderr tail is logged as a short diagnostic, never as a copy of a long native buffer.</summary>
public sealed class WorkerErrorTailTests
{
    [Fact]
    public void KeepsTheLastLinesJoined()
    {
        var tail = Enumerable.Range(1, 12).Select(i => "line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();

        Assert.Equal("line 5 | line 6 | line 7 | line 8 | line 9 | line 10 | line 11 | line 12", WorkerClient.FormatTail(tail));
    }

    [Fact]
    public void CutsEachLongLine()
    {
        var formatted = WorkerClient.FormatTail([new string('x', 5000), "ggml: out of memory"]);

        Assert.StartsWith(new string('x', WorkerClient.TailLineChars) + "… | ", formatted, StringComparison.Ordinal);
        Assert.EndsWith("ggml: out of memory", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void CapsTheTotalAndKeepsTheNewestText()
    {
        var tail = Enumerable.Repeat(new string('y', 1000), 7).Append("the last word").ToList();

        var formatted = WorkerClient.FormatTail(tail);

        Assert.True(formatted.Length <= WorkerClient.TailTotalChars + "(earlier text cut) …".Length);
        Assert.EndsWith("the last word", formatted, StringComparison.Ordinal);
        Assert.StartsWith("(earlier text cut) …", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCharactersCannotForgeLogLines()
    {
        Assert.Equal("abort  fake entry", WorkerClient.FormatTail(["abort\r\nfake entry"]));
    }
}
