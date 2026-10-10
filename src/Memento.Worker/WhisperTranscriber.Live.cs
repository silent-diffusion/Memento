using System.Text.RegularExpressions;
using System.Threading.Channels;
using Memento.Core.Workers;
using Memento.Transcription;
using Whisper.net.Logger;

namespace Memento.Worker;

/// <summary>
/// The live transcript while recording (2.0, <see cref="LiveJob"/>): the model is loaded once, then each <c>audio</c>
/// line (a 10-second window of the mix, 16 kHz) is heard whole and answered with a <c>heard</c> line, until <c>end</c>
/// or <c>cancel</c>. The windows are short and provisional, so there is no speech packing or alignment (the full pass,
/// unchanged, does that after Stop and replaces this draft); each window is heard without the previous one's text so a
/// mistake does not carry on. With <c>auto</c> the language is detected on the first window with speech and kept.
/// </summary>
internal sealed partial class WhisperTranscriber
{
    public async Task<WorkerReply> RunLiveAsync(LiveJob job, ChannelReader<WorkerCommand> commands, CancellationToken cancellationToken)
    {
        using var gpuLock = job.Runtimes.Contains(TranscriptionDefaults.RuntimeVulkan) ? GpuLock.Acquire(output, cancellationToken) : null;
        if (!File.Exists(job.ModelPath))
        {
            throw new WorkerFailure(WorkerErrorCodes.ModelLoad, $"the model file {Path.GetFileName(job.ModelPath)} is missing");
        }

        using var logging = LogProvider.AddLogger(OnNativeLog);
        var asTranscription = new TranscribeJob([], job.ModelPath, job.ModelId, job.Runtimes, job.GpuDevice, job.GpuName, job.Language, job.Prompt, job.Threads, false, 30, 0);
        var (factory, device) = LoadFactory(asTranscription);
        using var factoryScope = factory;
        output.Send(new WorkerReply { Type = WorkerMessageTypes.Device, Device = device });

        var detect = string.IsNullOrWhiteSpace(job.Language) || job.Language == "auto";
        await using var processor = factory.CreateBuilder()
            .WithThreads(Math.Max(1, job.Threads))
            .WithPrompt(job.Prompt)
            .WithNoContext()
            .WithEncoderBeginHandler(_ => !cancellationToken.IsCancellationRequested)
            .WithLanguage(detect ? "auto" : job.Language)
            .Build();

        string? language = detect ? null : job.Language;
        await foreach (var command in commands.ReadAllAsync(cancellationToken))
        {
            if (command.Type == WorkerMessageTypes.End)
            {
                break;
            }

            if (command.Type != WorkerMessageTypes.Audio || command.Audio is not { } audio)
            {
                continue;
            }

            float[] samples;
            try
            {
                samples = audio.Decode();
            }
            catch (FormatException)
            {
                throw new WorkerFailure(WorkerErrorCodes.InvalidJob, $"live window {audio.Window} is not base64 audio");
            }

            var heard = new List<WorkerSegment>();
            try
            {
                await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
                {
                    if (language is null && !string.IsNullOrWhiteSpace(segment.Language))
                    {
                        language = segment.Language;
                        processor.ChangeLanguage(language);
                    }

                    var text = (segment.Text ?? string.Empty).Trim();
                    if (text.Length == 0 || NotSpeech().IsMatch(text) || text == job.Prompt)
                    {
                        continue;
                    }

                    heard.Add(new WorkerSegment(
                        Math.Round(audio.StartSeconds + segment.Start.TotalSeconds, 2),
                        Math.Round(audio.StartSeconds + segment.End.TotalSeconds, 2),
                        text,
                        segment.MinProbability,
                        []));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Cancelled during a live window.", ex, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not WorkerFailure)
            {
                throw new WorkerFailure(WorkerErrorCodes.Engine, $"the engine failed on live window {audio.Window} ({ex.Message.TrimEnd('.')})", ex);
            }

            output.Send(new WorkerReply { Type = WorkerMessageTypes.Heard, Window = audio.Window, Segments = heard, Language = language });
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new WorkerReply { Type = WorkerMessageTypes.Result, Language = language };
    }

    /// <summary>What the engine writes for sounds that are not words: "[BLANK_AUDIO]", "(music)", "*laughs*".</summary>
    [GeneratedRegex(@"^\s*[\[\(\*♪].*[\]\)\*♪]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex NotSpeech();
}
