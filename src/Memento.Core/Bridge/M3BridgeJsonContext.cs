using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge;

/// <summary>
/// Source-generated serialization for the M3 contracts (BRIDGE.md M3), with the same options as
/// <see cref="BridgeJsonContext"/>: camelCase, unknown request fields rejected. Kept apart so the M3 methods register
/// their types in one place.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(RecordingIdParams))]
[JsonSerializable(typeof(Project))]
[JsonSerializable(typeof(AgendaImportFileParams))]
[JsonSerializable(typeof(AgendaImportDroppedParams))]
[JsonSerializable(typeof(AgendaParseTextParams))]
[JsonSerializable(typeof(AgendaImportResult))]
[JsonSerializable(typeof(AgendaPreviewResult))]
[JsonSerializable(typeof(AgendaApplyParams))]
[JsonSerializable(typeof(AgendaDiscardParams))]
[JsonSerializable(typeof(AgendaSetCoveredParams))]
[JsonSerializable(typeof(AgendaResult))]
[JsonSerializable(typeof(AttachmentsResult))]
[JsonSerializable(typeof(AttachmentsAddParams))]
[JsonSerializable(typeof(AttachmentAddResult))]
[JsonSerializable(typeof(AttachmentIdParams))]
[JsonSerializable(typeof(LibraryImportMediaParams))]
[JsonSerializable(typeof(LibraryImportMediaResult))]
[JsonSerializable(typeof(ProjectChangeTypeParams))]
[JsonSerializable(typeof(ExportEstimateParams))]
[JsonSerializable(typeof(ExportEstimate))]
[JsonSerializable(typeof(ExportRunParams))]
[JsonSerializable(typeof(JobIdParams))]
[JsonSerializable(typeof(JobIdResult))]
[JsonSerializable(typeof(LibraryUsage))]
[JsonSerializable(typeof(LibraryRebuildIndexResult))]
[JsonSerializable(typeof(LibraryMoveParams))]
[JsonSerializable(typeof(StorageReclaimParams))]
[JsonSerializable(typeof(AiSetKeyParams))]
[JsonSerializable(typeof(AiProviderParams))]
[JsonSerializable(typeof(AiKeyResult))]
[JsonSerializable(typeof(AppSetStartupParams))]
[JsonSerializable(typeof(AppStartupResult))]
[JsonSerializable(typeof(BridgeEventEnvelope<ExportProgressPayload>))]
[JsonSerializable(typeof(BridgeEventEnvelope<LibraryMoveProgressPayload>))]
[JsonSerializable(typeof(BridgeEventEnvelope<StorageReclaimProgressPayload>))]
public sealed partial class M3BridgeJsonContext : JsonSerializerContext
{
}
