using Memento.Audio.Recording;

namespace Memento.Audio.Tests.Recording;

public sealed class ReplayEventTests
{
    [Fact]
    public void AnEarlySubscriberGetsEachOccurrenceLiveAndNoReplay()
    {
        var replay = new ReplayEvent<EventArgs>();
        var seen = 0;
        EventHandler<EventArgs> handler = (_, _) => seen++;

        Assert.Empty(replay.Add(handler));
        var args = new EventArgs();
        replay.Record(args)!.Invoke(this, args);

        Assert.Equal(1, seen);
    }

    [Fact]
    public void ALateSubscriberGetsEveryEarlierOccurrenceInOrder()
    {
        var replay = new ReplayEvent<EventArgs>();
        var first = new EventArgs();
        var second = new EventArgs();

        Assert.Null(replay.Record(first));
        Assert.Null(replay.Record(second));
        var missed = replay.Add((_, _) => { });

        Assert.Equal([first, second], missed);
    }

    [Fact]
    public void ARemovedHandlerIsNotDeliveredTo()
    {
        var replay = new ReplayEvent<EventArgs>();
        EventHandler<EventArgs> handler = (_, _) => { };
        replay.Add(handler);

        replay.Remove(handler);

        Assert.Null(replay.Record(new EventArgs()));
        Assert.Empty(replay.Add(null));
    }
}
