using Memento.Core.Video;

namespace Memento.Core.Tests.Fakes;

/// <summary>Video files without Media Foundation: repairs report what they are told, joins and composes write small files.</summary>
internal sealed class FakeVideoFiles : IVideoFiles
{
    public bool IsAvailable => true;

    /// <summary>What <see cref="Repair"/> answers for every file.</summary>
    public VideoFileRepair RepairResult { get; set; } = new(true, 150, 5_000, 1234);

    public List<string> Repaired { get; } = [];

    public List<IReadOnlyList<VideoJoinPart>> Joins { get; } = [];

    public List<VideoComposeRequest> Composed { get; } = [];

    /// <summary>Joins report that the parts were encoded again.</summary>
    public bool JoinReencodes { get; set; }

    /// <summary>When set, answers (or throws) for a repaired file instead of <see cref="RepairResult"/>.</summary>
    public Func<string, VideoFileRepair>? RepairWith { get; set; }

    public VideoFileRepair Repair(string path)
    {
        lock (Repaired)
        {
            Repaired.Add(path);
        }

        return RepairWith is { } answer ? answer(path) : RepairResult;
    }

    public bool LastJoinReencoded { get; private set; }

    /// <summary>The next join fails with this.</summary>
    public Exception? FailNextJoin { get; set; }

    public async Task<VideoJoinResult> JoinAsync(IReadOnlyList<VideoJoinPart> parts, string outputPath, bool reencode, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Joins.Add(parts);
        LastJoinReencoded = reencode;
        if (FailNextJoin is { } fail)
        {
            FailNextJoin = null;
            progress?.Report(0.7);
            throw fail;
        }

        progress?.Report(0.5);
        await File.WriteAllBytesAsync(outputPath, new byte[parts.Count * 1000], cancellationToken);
        return new VideoJoinResult(600, 20_000, parts.Count * 1000, JoinReencodes);
    }

    public async Task ComposeAsync(VideoComposeRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        lock (Composed)
        {
            Composed.Add(request);
        }

        progress?.Report(0.5);
        cancellationToken.ThrowIfCancellationRequested();
        await File.WriteAllBytesAsync(request.OutputPath, new byte[2048], cancellationToken);
        progress?.Report(1);
    }
}
