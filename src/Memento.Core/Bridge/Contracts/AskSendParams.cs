using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>ask.send</c>: a question about one recording, for one provider (<c>null</c>: the Settings default, else the local model).</summary>
public sealed record AskSendParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string Question, string? ProviderId = null);