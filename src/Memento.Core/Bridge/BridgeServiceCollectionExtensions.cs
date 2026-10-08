using Memento.Core.Bridge.Methods;
using Memento.Core.Library;
using Memento.Core.Status;
using Memento.Core.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Core.Bridge;

/// <summary>Registers the bridge router, every M0 and M1 method and the event publishers.</summary>
public static class BridgeServiceCollectionExtensions
{
    public static IServiceCollection AddMementoBridge(this IServiceCollection services)
    {
        services.AddSingleton<IBridgeHandler, AppVersionMethod>();
        services.AddSingleton<IBridgeHandler, OpenExternalMethod>();
        services.AddSingleton<IBridgeHandler, SettingsGetMethod>();
        services.AddSingleton<IBridgeHandler, SettingsSetMethod>();
        services.AddSingleton<IBridgeHandler, LibraryListMethod>();
        services.AddSingleton<IBridgeHandler, UiReadyMethod>();

        services.AddSingleton<IBridgeHandler, LibraryProcessingMethod>();
        services.AddSingleton<IBridgeHandler, ProjectGetMethod>();
        services.AddSingleton<IBridgeHandler, ProjectUpdateDetailsMethod>();
        services.AddSingleton<IBridgeHandler, ProjectDeleteEstimateMethod>();
        services.AddSingleton<IBridgeHandler, ProjectDeleteMethod>();
        services.AddSingleton<IBridgeHandler, ProjectRenameMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsAddChapterMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsUpdateChapterMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsRemoveChapterMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsAddHighlightMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsUpdateHighlightMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsRemoveHighlightMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsAddTopicMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsRemoveTopicMethod>();
        services.AddSingleton<IBridgeHandler, SourcesListMethod>();
        services.AddSingleton<IBridgeHandler, RecordingStartMethod>();
        services.AddSingleton<IBridgeHandler, RecordingSetSourceMethod>();
        services.AddSingleton<IBridgeHandler, RecordingPauseMethod>();
        services.AddSingleton<IBridgeHandler, RecordingResumeMethod>();
        services.AddSingleton<IBridgeHandler, RecordingMarkHighlightMethod>();
        services.AddSingleton<IBridgeHandler, RecordingStopMethod>();
        services.AddSingleton<IBridgeHandler, RecordingCurrentMethod>();
        services.AddSingleton<IBridgeHandler, RecoveryListMethod>();
        services.AddSingleton<IBridgeHandler, RecoveryAcknowledgeMethod>();
        services.AddSingleton<IBridgeHandler, DialogPickFolderMethod>();
        services.AddSingleton<IBridgeHandler, StatusGetMethod>();

        services.AddSingleton<IBridgeHandler, TranscriptGetMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptEditSegmentMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptSetSegmentSpeakerMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptRenameSpeakerMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptMergeSpeakersMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptRestoreSpeakerMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptRemoveSpeakerMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptMarkReviewedMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptSearchMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptRetranscribeMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptVersionsMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptRestoreVersionMethod>();

        // The clipboard (after 1.2.0): the app registers the WPF clipboard first; tests and tools have none.
        services.TryAddSingleton<Host.IClipboard, Host.UnavailableClipboard>();
        services.AddSingleton<Export.TranscriptClipboard>();
        services.AddSingleton<IBridgeHandler, TranscriptCopyMethod>();

        services.AddSingleton<IBridgeHandler, TranscriptGetVersionMethod>();
        services.AddSingleton<IBridgeHandler, HistoryLinksMethod>();
        services.AddSingleton<IBridgeHandler, ProcessingRetryMethod>();
        services.AddSingleton<IBridgeHandler, ProcessingCancelMethod>();
        services.AddSingleton<IBridgeHandler, ProcessingPauseMethod>();
        services.AddSingleton<IBridgeHandler, ProcessingResumeMethod>();
        services.AddSingleton<IBridgeHandler, ModelsListMethod>();
        services.AddSingleton<IBridgeHandler, ModelsInstallMethod>();
        services.AddSingleton<IBridgeHandler, ModelsCancelInstallMethod>();
        services.AddSingleton<IBridgeHandler, ModelsRemoveMethod>();
        services.AddSingleton<IBridgeHandler, EngineStatusMethod>();
        services.AddSingleton<IBridgeHandler, EngineRefreshMethod>();

        services.AddMementoM3();

        // Self-update (H1): the app replaces the client with Velopack; tests and build folders keep "cannot update".
        services.AddSingleton<IBridgeHandler, UpdatesStatusMethod>();
        services.AddSingleton<IBridgeHandler, UpdatesCheckMethod>();
        services.AddSingleton<IBridgeHandler, UpdatesApplyMethod>();
        services.TryAddSingleton<IUpdateClient>(sp => new NoUpdateClient(sp.GetRequiredService<Host.IAppInfo>().Version));
        services.TryAddSingleton<UpdateStatusBoard>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<UpdateService>();

        services.AddSingleton<BridgeRouter>();
        services.AddSingleton<BridgeEventPublisher>();
        services.AddSingleton<FooterStatusService>();
        services.TryAddSingleton<RecordingStatusBoard>();
        services.TryAddSingleton<ILibraryLocation, SettingsLibraryLocation>();
        return services;
    }
}
