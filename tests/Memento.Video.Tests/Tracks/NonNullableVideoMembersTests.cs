using Memento.Core.Tests.Serialization;
using Memento.Video.Tracks;

namespace Memento.Video.Tests.Tracks;

/// <summary>
/// Security audit 2, §9, for the video track entries and checkpoints Memento.Video reads (from <c>project.json</c> and
/// <c>recording.state.json</c> during recovery): a list or object member the JSON leaves out keeps its default.
/// </summary>
public sealed class NonNullableVideoMembersTests
{
    [Fact]
    public void NoListOrObjectOfAVideoEntryReadsAsNullWhenTheJsonLeavesItOut()
    {
        var problems = NonNullableContracts.FindNullsReadFromEmptyObjects(VideoJsonContext.Default);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.Contains(typeof(Core.Projects.VideoTrack), NonNullableContracts.ObjectTypes(VideoJsonContext.Default));
    }
}
