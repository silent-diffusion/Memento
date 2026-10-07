using System.Text.Json;
using Memento.Core.Host;

namespace Memento.Tools.TranscriptionCheck;

/// <summary>Prints the bridge events a check cares about: model downloads and stage labels as they change.</summary>
internal sealed class ConsoleSink : IBridgeEventSink
{
    private readonly object _gate = new();
    private string? _lastStages;
    private DateTime _lastModels = DateTime.MinValue;

    public void Post(string json)
    {
        using var document = JsonDocument.Parse(json);
        var name = document.RootElement.GetProperty("event").GetString();
        var payload = document.RootElement.GetProperty("payload");
        lock (_gate)
        {
            switch (name)
            {
                case "models.progress":
                    var state = payload.GetProperty("state").GetString();
                    if (state is "downloading" && DateTime.UtcNow - _lastModels < TimeSpan.FromSeconds(5))
                    {
                        return;
                    }

                    _lastModels = DateTime.UtcNow;
                    Console.WriteLine($"  [models] {payload.GetProperty("modelId").GetString()} {state} {payload.GetProperty("percent").GetInt32()}% {payload.GetProperty("message")}");
                    break;
                case "processing.progress":
                    var stages = string.Join(", ", payload.GetProperty("stages").EnumerateArray().Select(s => $"{s.GetProperty("stage").GetString()}={s.GetProperty("state").GetString()} ({s.GetProperty("label")})"));
                    var coarse = string.Join(",", payload.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("state").GetString() + (s.GetProperty("percent").ValueKind == JsonValueKind.Number ? (s.GetProperty("percent").GetInt32() / 25).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty)));
                    if (coarse != _lastStages)
                    {
                        _lastStages = coarse;
                        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {stages}");
                    }

                    break;
                case "transcript.changed":
                    Console.WriteLine($"  [transcript] version {payload.GetProperty("version").GetInt32()} ({payload.GetProperty("reason").GetString()})");
                    break;
            }
        }
    }
}
