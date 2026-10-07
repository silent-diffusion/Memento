using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Processing;

/// <summary>Keeps a manifest's stage list in pipeline order: stored, transcript, speakers, minutes, optimize.</summary>
public static class StageList
{
    private static readonly string[] Order = [StageNames.Stored, StageNames.Transcript, StageNames.Speakers, StageNames.Minutes, StageNames.Optimize];

    /// <summary>The list with <paramref name="status"/> in its place (replacing any earlier entry for that stage).</summary>
    public static IReadOnlyList<StageStatus> With(IReadOnlyList<StageStatus> stages, StageStatus status)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(status);
        var list = stages.Where(s => s.Stage != status.Stage).ToList();
        list.Add(status);
        return Sorted(list);
    }

    /// <summary>The list without <paramref name="stage"/>.</summary>
    public static IReadOnlyList<StageStatus> Without(IReadOnlyList<StageStatus> stages, string stage)
    {
        ArgumentNullException.ThrowIfNull(stages);
        return stages.Where(s => s.Stage != stage).ToList();
    }

    public static StageStatus? Find(IReadOnlyList<StageStatus> stages, string stage) => stages.FirstOrDefault(s => s.Stage == stage);

    public static IReadOnlyList<ProjectStageFailure> WithFailure(IReadOnlyList<ProjectStageFailure> failures, ProjectStageFailure? failure, string stage) =>
        failures.Where(f => f.Stage != stage).Concat(failure is null ? [] : [failure]).ToList();

    private static List<StageStatus> Sorted(List<StageStatus> stages) =>
        stages.Select((s, i) => (Status: s, Index: i))
            .OrderBy(x => Array.IndexOf(Order, x.Status.Stage) is var at and >= 0 ? at : Order.Length - 1)
            .ThenBy(x => x.Index)
            .Select(x => x.Status)
            .ToList();
}
