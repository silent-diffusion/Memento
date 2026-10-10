using Memento.Core.Processing;
using Memento.Transcription.Stages;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Transcription;

/// <summary>
/// Registers the <c>transcript</c>, <c>speakers</c> and <c>topics</c> stages and the live transcript (they run their
/// engines in <c>Memento.Worker.exe</c>).
/// </summary>
public static class TranscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddMementoTranscription(this IServiceCollection services)
    {
        services.AddSingleton<TranscriptStage>();
        services.AddSingleton<IProcessingStage>(sp => sp.GetRequiredService<TranscriptStage>());
        services.AddSingleton<SpeakersStage>();
        services.AddSingleton<IProcessingStage>(sp => sp.GetRequiredService<SpeakersStage>());
        services.AddSingleton<TopicsStage>();
        services.AddSingleton<IProcessingStage>(sp => sp.GetRequiredService<TopicsStage>());

        // 2.0: the live transcript while recording (its loop is started by the app's hosted lifetime).
        services.AddSingleton<Live.LiveTranscriptService>();
        return services;
    }
}
