using System.Text;

namespace Memento.AI.Tests.Fakes;

/// <summary>One scripted answer of <see cref="ScriptedHttpServer"/>.</summary>
internal sealed record ScriptedResponse
{
    public int Status { get; init; } = 200;

    public string ContentType { get; init; } = "application/json";

    public IReadOnlyList<(string Name, string Value)> Headers { get; init; } = [];

    public IReadOnlyList<(string Text, TimeSpan Delay)> Parts { get; init; } = [];

    /// <summary>Read the request, then never answer.</summary>
    public bool StallBeforeHeaders { get; init; }

    /// <summary>Send everything, then keep the connection open without closing it.</summary>
    public bool StallAfterBody { get; init; }

    public static ScriptedResponse Json(int status, string body, params (string Name, string Value)[] headers) =>
        new() { Status = status, Headers = headers, Parts = [(body, TimeSpan.Zero)] };

    /// <summary>A server-sent event stream: each item is (event name, JSON data).</summary>
    public static ScriptedResponse Sse(params (string Event, string Data)[] events) =>
        new() { ContentType = "text/event-stream", Parts = events.Select(e => (Format(e.Event, e.Data), TimeSpan.Zero)).ToList() };

    public static ScriptedResponse Stall() => new() { StallBeforeHeaders = true };

    public static string Format(string name, string data)
    {
        var builder = new StringBuilder();
        if (name.Length > 0)
        {
            builder.Append("event: ").Append(name).Append('\n');
        }

        builder.Append("data: ").Append(data).Append("\n\n");
        return builder.ToString();
    }
}
