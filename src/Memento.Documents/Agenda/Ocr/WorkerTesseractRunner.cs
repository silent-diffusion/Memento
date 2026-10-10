using System.Collections.Concurrent;
using Memento.Core.Workers;

namespace Memento.Documents.Agenda.Ocr;

/// <summary>
/// <see cref="ITesseractRunner"/> on Memento.Worker: one <c>ocr</c> job per recognizer (processor only, so it never
/// waits for the graphics card), each image a <c>recognize</c> line answered by a <c>recognized</c> line.
/// </summary>
public sealed class WorkerTesseractRunner(WorkerClient workers) : ITesseractRunner
{
    /// <summary>How long a recognizer that was told to end may take before it is stopped.</summary>
    internal static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(5);

    public async Task<ITesseractSession> OpenAsync(string languageDataDirectory, string language, CancellationToken cancellationToken)
    {
        var session = new Session();
        session.Worker = await workers.OpenAsync(
            new WorkerJob(WorkerJobKinds.Ocr, Ocr: new OcrJob(languageDataDirectory, language)),
            session.OnReplyAsync,
            cancellationToken).ConfigureAwait(false);
        return session;
    }

    private sealed class Session : ITesseractSession
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<OcrRecognized>> _waiting = new();

        public WorkerSession? Worker { get; set; }

        public Task OnReplyAsync(WorkerReply reply)
        {
            if (reply.Type == WorkerMessageTypes.Recognized && reply.Ocr is { } recognized && _waiting.TryRemove(recognized.Id, out var waiting))
            {
                waiting.TrySetResult(recognized);
            }

            return Task.CompletedTask;
        }

        public async Task<OcrRecognized> RecognizeAsync(OcrImage image, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(image);
            var worker = Worker ?? throw new InvalidOperationException("The recognizer has not started.");
            var waiting = new TaskCompletionSource<OcrRecognized>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_waiting.TryAdd(image.Id, waiting))
            {
                throw new InvalidOperationException($"Image {image.Id} is already being read.");
            }

            try
            {
                await worker.SendAsync(new WorkerCommand(WorkerMessageTypes.Recognize, Image: image)).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The worker has gone; its completion says why.
            }

            var finished = await Task.WhenAny(waiting.Task, worker.Completion).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (finished == waiting.Task)
            {
                return await waiting.Task.ConfigureAwait(false);
            }

            _waiting.TryRemove(image.Id, out _);
            await worker.Completion.ConfigureAwait(false); // Throws the job's failure, if it failed.
            throw new WorkerCrashedException($"The recognizer ended without reading image {image.Id}.");
        }

        public async ValueTask DisposeAsync()
        {
            if (Worker is not { } worker)
            {
                return;
            }

            if (!worker.Completion.IsCompleted)
            {
                try
                {
                    await worker.SendAsync(new WorkerCommand(WorkerMessageTypes.End)).ConfigureAwait(false);
                    await worker.Completion.WaitAsync(EndTimeout).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Ending is best effort; disposing the worker session stops one that did not end.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }

            await worker.DisposeAsync().ConfigureAwait(false);
        }
    }
}
