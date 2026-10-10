using Memento.Core.Bridge.Methods;
using Memento.Core.Transcripts;
using Memento.Core.Voices;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Core.Bridge;

/// <summary>
/// The 2.0 Review pieces (DESIGN.md §19): known voices, suggested chapters and the selection mode's several-lines speaker
/// change, with their bridge methods. Called by <see cref="BridgeServiceCollectionExtensions.AddMementoBridge"/>; the
/// services they use (<see cref="TranscriptStore"/>, <see cref="TranscriptService"/>) come from <c>AddMementoLibrary</c>.
/// </summary>
public static class ReviewServiceCollectionExtensions
{
    public static IServiceCollection AddMementoReview(this IServiceCollection services)
    {
        services.AddSingleton<KnownVoicesStore>();
        services.AddSingleton<KnownVoiceService>();
        services.AddSingleton<ChapterSuggestionService>();

        services.AddSingleton<IBridgeHandler, VoicesListMethod>();
        services.AddSingleton<IBridgeHandler, VoicesSetSuggestMethod>();
        services.AddSingleton<IBridgeHandler, VoicesForgetMethod>();
        services.AddSingleton<IBridgeHandler, VoicesForgetAllMethod>();
        services.AddSingleton<IBridgeHandler, VoicesRememberMethod>();
        services.AddSingleton<IBridgeHandler, VoicesRevertMethod>();
        services.AddSingleton<IBridgeHandler, VoicesMatchesMethod>();
        services.AddSingleton<IBridgeHandler, VoicesDeclineMethod>();
        services.AddSingleton<IBridgeHandler, VoicesAcceptMatchMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsSuggestChaptersMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsDismissSuggestionMethod>();
        services.AddSingleton<IBridgeHandler, AnnotationsRestoreSuggestionMethod>();
        services.AddSingleton<IBridgeHandler, TranscriptSetSegmentsSpeakerMethod>();
        return services;
    }
}
