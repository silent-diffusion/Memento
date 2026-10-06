using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Tests.Projects;

/// <summary>The Library row's stage visibility rule (BRIDGE.md › RecordingSummary.stages).</summary>
public sealed class ProjectMapperTests
{
    private static StageStatus Stage(string stage, string state, int? percent = null) => new(stage, state, percent, null);

    private static List<string> Names(IReadOnlyList<StageStatus> stages) => stages.Select(s => $"{s.Stage}:{s.State}").ToList();

    [Fact]
    public void FinishedStoredAndOptimizeStagesAreLeftOut()
    {
        Assert.Empty(ProjectMapper.VisibleStages([Stage(StageNames.Stored, StageStates.Done)]));
        Assert.Empty(ProjectMapper.VisibleStages([Stage(StageNames.Stored, StageStates.Done), Stage(StageNames.Optimize, StageStates.Done)]));
    }

    [Theory]
    [InlineData(StageStates.Active)]
    [InlineData(StageStates.Queued)]
    [InlineData(StageStates.Failed)]
    public void RunningOrFailedStoredAndOptimizeStagesAreListed(string state)
    {
        Assert.Equal([$"stored:{state}"], Names(ProjectMapper.VisibleStages([Stage(StageNames.Stored, state)])));
        Assert.Equal(
            [$"optimize:{state}"],
            Names(ProjectMapper.VisibleStages([Stage(StageNames.Stored, StageStates.Done), Stage(StageNames.Optimize, state)])));
    }

    [Fact]
    public void OtherStagesAreListedInEveryStateAndKeepTheirOrder()
    {
        var stages = new[]
        {
            Stage(StageNames.Stored, StageStates.Done),
            Stage(StageNames.Transcript, StageStates.Done),
            Stage(StageNames.Speakers, StageStates.Failed),
            Stage(StageNames.Minutes, StageStates.Queued),
            Stage(StageNames.Optimize, StageStates.Done),
        };

        Assert.Equal(["transcript:done", "speakers:failed", "minutes:queued"], Names(ProjectMapper.VisibleStages(stages)));
    }

    [Fact]
    public void ASummaryIsProcessingAndShowsOptimizeWhileItRuns()
    {
        var summary = ProjectMapper.BuildSummary(
            "20261006-100000-k3f9ab",
            "Sync",
            "meeting",
            DateTimeOffset.UnixEpoch,
            1000,
            false,
            [],
            [Stage(StageNames.Stored, StageStates.Done), Stage(StageNames.Optimize, StageStates.Active, 40)],
            ProjectStates.Ready,
            0);

        Assert.True(summary.IsProcessing);
        Assert.Equal(["optimize:active"], Names(summary.Stages));
    }
}
