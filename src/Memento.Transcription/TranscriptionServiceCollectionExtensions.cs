using Memento.Core.Processing;
using Memento.Transcription.Stages;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Transcription;

/// <summary>Registers the <c>transcript</c>, <c>speakers</c> and <c>topics</c> stages (they run their engines in <c>Memento.Worker.exe</c>).</summary>
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
        return services;
    }
}
