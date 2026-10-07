using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>documents.restoreVersion</c>.</summary>
public sealed record DocumentRestoreParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string DocumentId, [property: JsonRequired] string VersionId);
