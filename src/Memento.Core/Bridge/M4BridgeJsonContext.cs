using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge;

/// <summary>
/// Source-generated serialization for the M4 contracts (BRIDGE.md M4), with the same options as
/// <see cref="BridgeJsonContext"/>: camelCase, unknown request fields rejected. Kept apart, as for M3, so the M4 methods
/// register their types in one place.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(RecordingIdParams))]
[JsonSerializable(typeof(JobIdParams))]
[JsonSerializable(typeof(ModulesListResult))]
[JsonSerializable(typeof(Template))]
[JsonSerializable(typeof(TemplatesListResult))]
[JsonSerializable(typeof(TemplateIdParams))]
[JsonSerializable(typeof(TemplateSaveParams))]
[JsonSerializable(typeof(Style))]
[JsonSerializable(typeof(StylesListResult))]
[JsonSerializable(typeof(StyleIdParams))]
[JsonSerializable(typeof(StyleSaveParams))]
[JsonSerializable(typeof(StyleSampleParams))]
[JsonSerializable(typeof(HtmlResult))]
[JsonSerializable(typeof(ProvidersListResult))]
[JsonSerializable(typeof(GenerationTemplateParams))]
[JsonSerializable(typeof(GenerationPreviewResult))]
[JsonSerializable(typeof(GenerationPreviewHtmlParams))]
[JsonSerializable(typeof(GenerationStartParams))]
[JsonSerializable(typeof(GenerationStartResult))]
[JsonSerializable(typeof(GenerationConfirmParams))]
[JsonSerializable(typeof(DocumentsListResult))]
[JsonSerializable(typeof(DocumentParams))]
[JsonSerializable(typeof(DocumentGetResult))]
[JsonSerializable(typeof(DocumentContent))]
[JsonSerializable(typeof(DocumentSummary))]
[JsonSerializable(typeof(DocumentRenderParams))]
[JsonSerializable(typeof(DocumentCreateParams))]
[JsonSerializable(typeof(DocumentSaveEditParams))]
[JsonSerializable(typeof(DocumentSaveEditResult))]
[JsonSerializable(typeof(DocumentNameParams))]
[JsonSerializable(typeof(DocumentVersionsResult))]
[JsonSerializable(typeof(DocumentRestoreParams))]
[JsonSerializable(typeof(DocumentRestoreResult))]
[JsonSerializable(typeof(DocumentExportParams))]
[JsonSerializable(typeof(DocumentFileResult))]
[JsonSerializable(typeof(DocumentCopyResult))]
[JsonSerializable(typeof(GenerationRecord))]
[JsonSerializable(typeof(BridgeEventEnvelope<GenerationProgress>))]
[JsonSerializable(typeof(BridgeEventEnvelope<GenerationOutput>))]
[JsonSerializable(typeof(BridgeEventEnvelope<DocumentsChangedPayload>))]
[JsonSerializable(typeof(BridgeEventEnvelope<EmptyPayload>))]
public sealed partial class M4BridgeJsonContext : JsonSerializerContext
{
}
