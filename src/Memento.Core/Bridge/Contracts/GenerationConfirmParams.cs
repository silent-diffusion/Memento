using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>generation.confirm</c>: the answer to "ask before every send".</summary>
public sealed record GenerationConfirmParams([property: JsonRequired] string JobId, [property: JsonRequired] bool Approved);
