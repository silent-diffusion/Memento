using Memento.Core.Workers;

namespace Memento.Transcription.Live;

/// <summary>The live transcript's worker of one session (2.0): its model, where it runs, and the window it is hearing.</summary>
internal sealed class LiveWorker(string modelId, string modelName, IDisposable lease)
{
    private readonly object _sync = new();
    private int _expected = -1;
    private TaskCompletionSource<IReadOnlyList<WorkerSegment>>? _reply;

    public string ModelId { get; } = modelId;

    /// <summary>"Small" or "Base", for the card's pill.</summary>
    public string ModelName { get; } = modelName.Replace("Whisper ", string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps the model from being removed while it is loaded.</summary>
    public IDisposable Lease { get; } = lease;

    public WorkerSession? Session { get; set; }

    public bool OnGpu { get; set; }

    /// <summary>"GPU" or "CPU" once the worker said where the model loaded.</summary>
    public string? Device { get; set; }

    /// <summary>It is ending because another job asked for the graphics card.</summary>
    public bool LettingGo { get; set; }

    /// <summary>Completes with the lines of <paramref name="window"/> when its <c>heard</c> line arrives.</summary>
    public Task<IReadOnlyList<WorkerSegment>> Expect(int window)
    {
        lock (_sync)
        {
            _expected = window;
            _reply = new TaskCompletionSource<IReadOnlyList<WorkerSegment>>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _reply.Task;
        }
    }

    public Task OnReplyAsync(WorkerReply reply)
    {
        if (reply.Type == WorkerMessageTypes.Device && reply.Device is { } device)
        {
            OnGpu = device.Runtime != WorkerRuntimes.Cpu;
            Device = OnGpu ? "GPU" : "CPU";
        }
        else if (reply.Type == WorkerMessageTypes.Heard)
        {
            lock (_sync)
            {
                if (reply.Window == _expected)
                {
                    _reply?.TrySetResult(reply.Segments ?? []);
                }
            }
        }

        return Task.CompletedTask;
    }
}
