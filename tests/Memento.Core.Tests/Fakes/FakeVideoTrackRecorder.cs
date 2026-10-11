using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Video;

namespace Memento.Core.Tests.Fakes;

/// <summary>One fake video track: a small file in <c>tracks/</c>, and a log of what it was asked to do.</summary>
internal sealed class FakeVideoTrackRecorder : IVideoTrackRecorder
{
    private readonly object _gate = new();
    private readonly string _path;
    private string _sourceName;
    private string _state = "recording";

    public FakeVideoTrackRecorder(VideoTrackRequest request, string sourceName)
    {
        Request = request;
        SourceId = request.SourceId;
        _sourceName = sourceName;
        _path = Path.Combine(request.ProjectFolder, "tracks", request.TrackId + ".mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, new byte[4096]);
    }

    public event EventHandler<VideoProblemEventArgs>? Problem;

    public VideoTrackRequest Request { get; }

    public string TrackId => Request.TrackId;

    public string Role => Request.Role;

    public string SourceId { get; private set; }

    public int Pauses { get; private set; }

    public int Resumes { get; private set; }

    public int Flushes { get; private set; }

    public List<string> Switches { get; } = [];

    public List<string> Qualities { get; } = [];

    public bool Stopped { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>The next switch fails with this.</summary>
    public VideoStartException? FailNextSwitch { get; set; }

    public void PauseTrack()
    {
        lock (_gate)
        {
            Pauses++;
            _state = "paused";
        }
    }

    public void ResumeTrack()
    {
        lock (_gate)
        {
            Resumes++;
            _state = "recording";
        }
    }

    public Task SetQualityAsync(string quality, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Qualities.Add(quality);
        }

        return Task.CompletedTask;
    }

    public Task SwitchSourceAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (FailNextSwitch is { } fail)
        {
            FailNextSwitch = null;
            return Task.FromException(fail);
        }

        lock (_gate)
        {
            Switches.Add(sourceId);
            SourceId = sourceId;
            _sourceName = FakeVideoCapture.NameOf(sourceId);
        }

        return Task.CompletedTask;
    }

    public VideoTrackCheckpoint Checkpoint(bool flush)
    {
        lock (_gate)
        {
            if (flush)
            {
                Flushes++;
            }

            return new VideoTrackCheckpoint(TrackId, Role, VideoSourceIds.KindOf(SourceId)!, SourceId, _sourceName, 1920, 1080, 30, [Part()], 4096, 300, 0, null, null);
        }
    }

    public VideoTrackStatus Status()
    {
        lock (_gate)
        {
            return new VideoTrackStatus(TrackId, _state, SourceId, _sourceName, null, "Test encoder", true, 1920, 1080, 30, 7_800_000, 2_000_000, 300, 1, 4096, 10_000);
        }
    }

    /// <summary>The source went away or the encoder failed, as Memento.Video would say it.</summary>
    public void Raise(string state, string code, string message, long atMs, string? detail = null)
    {
        lock (_gate)
        {
            _state = state;
        }

        Problem?.Invoke(this, new VideoProblemEventArgs(TrackId, Role, state, code, SourceId, _sourceName, detail, message, atMs));
    }

    public Task<VideoTrack> StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Stopped = true;
            var ended = _state == "ended";
            _state = "stopped";
            return Task.FromResult(new VideoTrack
            {
                Id = TrackId,
                Role = Role,
                SourceKind = VideoSourceIds.KindOf(SourceId)!,
                SourceId = SourceId,
                Name = _sourceName,
                File = $"tracks/{TrackId}.mp4",
                Parts = [Part()],
                Width = 1920,
                Height = 1080,
                FrameRate = 30,
                Encoder = "Test encoder",
                HardwareEncoder = true,
                BitrateBitsPerSecond = 7_800_000,
                StartOffsetMs = 40,
                DurationMs = 10_000,
                FramesWritten = 300,
                FramesDropped = 1,
                EndedEarlyAtMs = ended ? 5_000 : null,
                EndReason = ended ? "sourceLost" : null,
            });
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private VideoTrackPart Part() => new($"tracks/{TrackId}.mp4", 40, 10_000, "Test encoder", true, 300);
}
