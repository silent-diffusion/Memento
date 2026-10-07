using System.Text.Json;

namespace Memento.AI.Tests.Fakes;

/// <summary>A request the fake server read: method, path, headers and body.</summary>
internal sealed record RecordedRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}
