using System.Text.Json;

namespace Memento.AI.Tests.Fakes;

/// <summary>Messages API event streams in the documented shape (message_start … message_stop).</summary>
internal static class AnthropicStreams
{
    public static ScriptedResponse Text(IEnumerable<string> deltas, string stopReason = "end_turn", string model = "claude-opus-5-5", int input = 120, int output = 9, bool withThinking = false, bool withFallback = false)
    {
        var events = new List<(string, string)>
        {
            ("message_start", $$$"""{"type":"message_start","message":{"id":"msg_test","type":"message","role":"assistant","model":"{{{model}}}","content":[],"stop_reason":null,"usage":{"input_tokens":{{{input}}},"output_tokens":1,"cache_read_input_tokens":0} } }"""),
            ("ping", """{"type":"ping"}"""),
        };
        var index = 0;
        if (withFallback)
        {
            events.Add(("content_block_start", $$$"""{"type":"content_block_start","index":{{{index}}},"content_block":{"type":"fallback","from":{"model":"claude-opus-5-5"},"to":{"model":"{{{model}}}"} } }"""));
            events.Add(("content_block_stop", $$$"""{"type":"content_block_stop","index":{{{index}}}}"""));
            index++;
        }

        if (withThinking)
        {
            events.Add(("content_block_start", $$$"""{"type":"content_block_start","index":{{{index}}},"content_block":{"type":"thinking","thinking":""}}"""));
            events.Add(("content_block_delta", $$$"""{"type":"content_block_delta","index":{{{index}}},"delta":{"type":"thinking_delta","thinking":""}}"""));
            events.Add(("content_block_delta", $$$"""{"type":"content_block_delta","index":{{{index}}},"delta":{"type":"signature_delta","signature":"c2lnbmF0dXJl"}}"""));
            events.Add(("content_block_stop", $$$"""{"type":"content_block_stop","index":{{{index}}}}"""));
            index++;
        }

        events.Add(("content_block_start", $$$"""{"type":"content_block_start","index":{{{index}}},"content_block":{"type":"text","text":""}}"""));
        foreach (var delta in deltas)
        {
            events.Add(("content_block_delta", $$$"""{"type":"content_block_delta","index":{{{index}}},"delta":{"type":"text_delta","text":{{{JsonSerializer.Serialize(delta)}}}}}"""));
        }

        events.Add(("content_block_stop", $$$"""{"type":"content_block_stop","index":{{{index}}}}"""));
        events.Add(("message_delta", $$$"""{"type":"message_delta","delta":{"stop_reason":"{{{stopReason}}}","stop_sequence":null},"usage":{"output_tokens":{{{output}}}}}"""));
        events.Add(("message_stop", """{"type":"message_stop"}"""));
        return ScriptedResponse.Sse([.. events]);
    }

    public static ScriptedResponse Error(int status, string type, string message, params (string Name, string Value)[] headers) =>
        ScriptedResponse.Json(status, $$$"""{"type":"error","error":{"type":"{{{type}}}","message":{{{JsonSerializer.Serialize(message)}}}},"request_id":"req_test"}""", headers);

    public static ScriptedResponse OverloadedInStream() => ScriptedResponse.Sse(
        ("message_start", """{"type":"message_start","message":{"id":"msg_test","model":"claude-opus-5-5","usage":{"input_tokens":5,"output_tokens":0}}}"""),
        ("error", """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}"""));
}
