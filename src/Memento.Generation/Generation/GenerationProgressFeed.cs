using System.Threading.Channels;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;

namespace Memento.Generation.Generation;

/// <summary>
/// One job's <c>generation.progress</c> events as one ordered feed, delivered in the order they were raised. Every report goes into one
/// unbounded channel with a single reader that applies the throttle (at most four a second; every stage change and the
/// final event always pass) and publishes; the final event is written last and closes the channel, so nothing raised
/// afterwards (a progress callback trailing the job) is ever sent. Reporting never blocks the pipeline.
/// </summary>
public sealed class GenerationProgressFeed
{
    private readonly Channel<Item> _channel = Channel.CreateUnbounded<Item>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly object _order = new();
    private readonly Action<GenerationProgress> _publish;
    private readonly ProgressThrottle _throttle;
    private readonly Task _consumer;
    private bool _closed;

    public GenerationProgressFeed(Action<GenerationProgress> publish, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(time);
        _publish = publish;
        _throttle = new ProgressThrottle(time);
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>Completes when the final event has been published.</summary>
    public Task Completion => _consumer;

    /// <summary>Queues a progress event; <paramref name="force"/> lets it past the throttle. Ignored after the final event.</summary>
    /// <returns><c>false</c> when the stream is already closed.</returns>
    public bool Report(GenerationProgress progress, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (_order)
        {
            return !_closed && _channel.Writer.TryWrite(new Item(progress, force, Final: false));
        }
    }

    /// <summary>Queues the final event after everything raised before it and closes the stream; a second final is ignored.</summary>
    /// <returns>A task that completes once the final event has been published.</returns>
    public Task CompleteAsync(GenerationProgress final)
    {
        ArgumentNullException.ThrowIfNull(final);
        lock (_order)
        {
            if (!_closed)
            {
                _closed = true;
                _channel.Writer.TryWrite(new Item(final, Force: true, Final: true));
                _channel.Writer.TryComplete();
            }
        }

        return _consumer;
    }

    private async Task ConsumeAsync()
    {
        string? lastStage = null;
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (!item.Final && !_throttle.TryPass(item.Force || !string.Equals(item.Progress.Stage, lastStage, StringComparison.Ordinal)))
            {
                continue;
            }

            lastStage = item.Progress.Stage;
            try
            {
                _publish(item.Progress);
            }
#pragma warning disable CA1031 // A failing sink must not stop the events that follow, above all the final one.
            catch (Exception)
#pragma warning restore CA1031
            {
            }

            if (item.Final)
            {
                return;
            }
        }
    }

    private readonly record struct Item(GenerationProgress Progress, bool Force, bool Final);
}
