using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary>2.0, <c>documents.copyModule</c>: one module of a document (the Follow-up email).</summary>
/// <param name="ModuleId">The document module's id (<c>DocumentModule.id</c>).</param>
public sealed record DocumentModuleParams([property: JsonRequired] string RecordingId, [property: JsonRequired] string DocumentId, [property: JsonRequired] string ModuleId);
