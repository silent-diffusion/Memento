using Memento.AI.Payload;
using Memento.Generation.Bridge;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Units;

/// <summary>The preview, the send confirmation and the record name only the inputs the payload holds.</summary>
public sealed class InputsUsedTests
{
    [Fact]
    public void ATickedInputWithNothingInItIsNotUsed()
    {
        // The synthetic meeting has a transcript, details, participants and an agenda, but no highlights or attachments.
        var selection = SyntheticMeeting.AllInputs with { Attachments = true };
        var payload = PayloadComposer.Compose(SyntheticMeeting.Material().ToPayloadInputs(null), selection);

        var used = M4Mapping.Used(selection, payload);

        Assert.True(used.Transcript);
        Assert.True(used.Details);
        Assert.True(used.Participants);
        Assert.True(used.Agenda);
        Assert.False(used.Highlights);
        Assert.False(used.Attachments);
        Assert.False(used.PreviousDocuments);
    }

    [Fact]
    public void AnInputThatWasNotTickedIsNeverUsed()
    {
        var selection = SyntheticMeeting.AllInputs with { Agenda = false, Participants = false };
        var payload = PayloadComposer.Compose(SyntheticMeeting.Material().ToPayloadInputs(null), selection);

        var used = M4Mapping.Used(selection, payload);

        Assert.False(used.Agenda);
        Assert.False(used.Participants);
        Assert.True(used.Transcript);
    }
}
