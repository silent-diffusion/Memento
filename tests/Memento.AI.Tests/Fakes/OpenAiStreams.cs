using System.Text.Json;

namespace Memento.AI.Tests.Fakes;

/// <summary>Responses API event streams (response.created … response.completed).</summary>
internal static class OpenAiStreams
{
    public static ScriptedResponse Text(IEnumerable<string> deltas, string status = "completed", string? incompleteReason = null, string model = "gpt-6-astra", int input = 80, int output = 6, bool refusal = false)
    {
        var events = new List<(string, string)>
        {
            ("response.created", $$$"""{"type":"response.created","response":{"id":"resp_test","status":"in_progress","model":"{{{model}}}"}}"""),
        };
        foreach (var delta in deltas)
        {
            events.Add(("response.output_text.delta", $$$"""{"type":"response.output_text.delta","item_id":"msg_1","output_index":0,"content_index":0,"delta":{{{JsonSerializer.Serialize(delta)}}}}"""));
        }

        if (refusal)
        {
            events.Add(("response.refusal.delta", """{"type":"response.refusal.delta","delta":"I can't help with that."}"""));
        }

        var details = incompleteReason is null ? "null" : $$$"""{"reason":"{{{incompleteReason}}}"}""";
        events.Add(($"response.{status}", $$$"""{"type":"response.{{{status}}}","response":{"id":"resp_test","status":"{{{status}}}","model":"{{{model}}}","incomplete_details":{{{details}}},"usage":{"input_tokens":{{{input}}},"input_tokens_details":{"cached_tokens":0},"output_tokens":{{{output}}} } } }"""));
        return ScriptedResponse.Sse([.. events]);
    }

    public static ScriptedResponse Error(int status, string type, string? code, string message, params (string Name, string Value)[] headers) =>
        ScriptedResponse.Json(status, $$$"""{"error":{"message":{{{JsonSerializer.Serialize(message)}}},"type":"{{{type}}}","param":null,"code":{{{(code is null ? "null" : JsonSerializer.Serialize(code))}}} } }""", headers);
}
