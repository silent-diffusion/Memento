using Memento.Audio;
using Memento.Video.Sources;

namespace Memento.Video.Tests.Fakes;

/// <summary>A source without a GPU: it has a picture once <see cref="HasPicture"/> is set, and raises loss and pauses on demand.</summary>
internal sealed class FakeVideoSource(VideoSourceId id, string name) : IVideoSource
{
    private long _sequence;

    public VideoSourceId Id { get; } = id;

    public string Name { get; } = name;

    public VideoSourceState State { get; set; } = VideoSourceState.Created;

    public bool HasPicture { get; set; } = true;

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public VideoCaptureException? FailOnStart { get; set; }

    public VideoSourceStatistics Statistics => new(_sequence, 0, 0, LatencyStatistics.Empty, 1920, 1080);

    public VideoCaptureException? Loss { get; private set; }

    public event EventHandler<VideoSourceLostEventArgs>? Lost;

    public event EventHandler<VideoSourceStateChangedEventArgs>? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (FailOnStart is not null)
        {
            throw FailOnStart;
        }

        Started = true;
        State = VideoSourceState.Running;
        return Task.CompletedTask;
    }

    public bool TryGetLatest(out VideoFrame frame)
    {
        if (!HasPicture)
        {
            frame = default;
            return false;
        }

        frame = new VideoFrame(null, 0, 1920, 1080, QpcClock.Now, ++_sequence);
        return true;
    }

    public void Lose(VideoErrorCode code, string message)
    {
        Loss = new VideoCaptureException(code, message);
        State = VideoSourceState.Lost;
        Lost?.Invoke(this, new VideoSourceLostEventArgs(Id, Loss, QpcClock.Now));
    }

    public void SetPaused(bool paused)
    {
        State = paused ? VideoSourceState.Paused : VideoSourceState.Running;
        StateChanged?.Invoke(this, new VideoSourceStateChangedEventArgs(State, paused ? "Window minimised" : null, QpcClock.Now));
    }

    public Task StopAsync()
    {
        Stopped = true;
        State = VideoSourceState.Stopped;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
