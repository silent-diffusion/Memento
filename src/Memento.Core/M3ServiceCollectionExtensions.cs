using Memento.Core.Agendas;
using Memento.Core.Attachments;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Methods;
using Memento.Core.Export;
using Memento.Core.Host;
using Memento.Core.Import;
using Memento.Core.Maintenance;
using Memento.Core.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Core;

/// <summary>
/// Registers M3 (BRIDGE.md M3): agenda import, attachments, media import, export, the remaining Settings and their
/// bridge methods. Call after <c>AddMementoBridge</c> and <c>AddMementoLibrary</c>. Host pieces registered with
/// TryAdd (file picker, agenda reader, media decoder, startup entry, secrets file) can be replaced before or after:
/// Memento.Documents adds the agenda reader, Memento.Audio the Media Foundation decoder, the app the WPF picker.
/// </summary>
public static class M3ServiceCollectionExtensions
{
    public static IServiceCollection AddMementoM3(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFilePicker, UnavailableFilePicker>();
        services.TryAddSingleton<IAgendaReader, UnavailableAgendaReader>();
        services.TryAddSingleton<IMediaDecoder, WavMediaDecoder>();
        services.TryAddSingleton<IStartupRegistration, RegistryStartupRegistration>();
        services.TryAddSingleton(SecretStoreOptions.Default);
        services.TryAddSingleton<ISecretStore, DpapiSecretStore>();
        services.TryAddSingleton(PendingAgendaOptions.Default);
        services.TryAddSingleton(Export.ExportJournalOptions.Default);
        services.TryAddSingleton<Export.ExportJournal>();
        services.TryAddSingleton<Export.InterruptedExports>();

        services.AddSingleton<M3EventPublisher>();
        services.AddSingleton<SettingsExtras>();
        services.AddSingleton<LibraryActivity>();
        services.AddSingleton<PendingAgendaFiles>();
        services.AddSingleton<DroppedFiles>();
        services.AddSingleton<AttachmentService>();
        services.AddSingleton<AgendaService>();
        services.AddSingleton<MediaImportService>();
        services.AddSingleton<ExportStatusBoard>();
        services.AddSingleton<ExportAudio>();
        services.AddSingleton<ExportPlanner>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<LibraryUsageService>();
        services.AddSingleton<LibraryMoveService>();
        services.AddSingleton<StorageReclaimService>();
        services.AddSingleton<ProjectTypeService>();

        services.AddSingleton<IBridgeHandler, AgendaImportFileMethod>();
        services.AddSingleton<IBridgeHandler, AgendaImportDroppedMethod>();
        services.AddSingleton<IBridgeHandler, AgendaParseTextMethod>();
        services.AddSingleton<IBridgeHandler, AgendaApplyMethod>();
        services.AddSingleton<IBridgeHandler, AgendaDiscardMethod>();
        services.AddSingleton<IBridgeHandler, AgendaSetCoveredMethod>();
        services.AddSingleton<IBridgeHandler, AttachmentsListMethod>();
        services.AddSingleton<IBridgeHandler, AttachmentsAddMethod>();
        services.AddSingleton<IBridgeHandler, AttachmentsRemoveMethod>();
        services.AddSingleton<IBridgeHandler, AttachmentsOpenMethod>();
        services.AddSingleton<IBridgeHandler, LibraryImportMediaMethod>();
        services.AddSingleton<IBridgeHandler, ProjectChangeTypeMethod>();
        services.AddSingleton<IBridgeHandler, ExportEstimateMethod>();
        services.AddSingleton<IBridgeHandler, ExportRunMethod>();
        services.AddSingleton<IBridgeHandler, ExportCancelMethod>();
        services.AddSingleton<IBridgeHandler, ExportOpenFolderMethod>();
        services.AddSingleton<IBridgeHandler, LibraryUsageMethod>();
        services.AddSingleton<IBridgeHandler, LibraryRebuildIndexMethod>();
        services.AddSingleton<IBridgeHandler, LibraryMoveMethod>();
        services.AddSingleton<IBridgeHandler, StorageReclaimMethod>();
        services.AddSingleton<IBridgeHandler, StorageKeepOnlyMixMethod>();
        services.AddSingleton<IBridgeHandler, AiSetKeyMethod>();
        services.AddSingleton<IBridgeHandler, AiClearKeyMethod>();
        services.AddSingleton<IBridgeHandler, AppSetStartupMethod>();
        return services;
    }
}
