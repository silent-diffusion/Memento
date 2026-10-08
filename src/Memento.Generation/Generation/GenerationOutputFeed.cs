using System.Text;
using System.Threading.Channels;
using Memento.AI;
using Memento.Core.Bridge.Contracts;

namespace Memento.Generation.Generation;

/// <summary>
/// One job's <c>generation.output</c> events (the Live output sheet), in the order they were raised. Every call queues
/// an item in one unbounded channel (never blocking the caller, which for the local model is the worker's reader) and a
/// single reader publishes them. Token events are coalesced there: the text of a pass waits until
/// <see cref="TokenInterval"/> has passed since the last token event was sent, and any other event first sends what
/// waits, so the UI gets at most about ten token events a second and every reply after its tokens. <c>done</c> is last
/// and closes the feed. The text passes through; nothing is kept once the pass has its reply, and nothing is written.
/// </summary>
public sealed class GenerationOutputFeed
{
    /// <summary>At most one token event per pass this often (the UI repaints the open pass at this rate).</summary>
    public static readonly TimeSpan TokenInterval = TimeSpan.FromMilliseconds(100);

    public const string StepKind = "step";
    public const string RequestKind = "request";
    public const string TokenKind = "token";
    public const string ReplyKind = "reply";
    public const string DoneKind = "done";

    public const string SegmentStep = "segment";
    public const string MapStep = "map";
    public const string ReduceStep = "reduce";
    public const string VerifyStep = "verify";
    public const string GroundingStep = "grounding";

    private readonly Channel<GenerationOutput> _channel = Channel.CreateUnbounded<GenerationOutput>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly object _order = new();
    private readonly string _jobId;
    private readonly Action<GenerationOutput> _publish;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Open> _open = new(StringComparer.Ordinal);
    private readonly Task _consumer;
    private int _passes;
    private bool _closed;

    public GenerationOutputFeed(string jobId, Action<GenerationOutput> publish, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(time);
        _jobId = jobId;
        _publish = publish;
        _time = time;
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>Completes when <c>done</c> has been published.</summary>
    public Task Completion => _consumer;

    /// <summary>A step done in code (segment, reduce, grounding), with what it did and how long it took.</summary>
    public void Step(string step, string title, string summary, TimeSpan elapsed)
    {
        lock (_order)
        {
            Write(new GenerationOutput(_jobId, NextId(), StepKind, step, title, summary) { ElapsedMs = (long)elapsed.TotalMilliseconds });
        }
    }

    /// <summary>A pass starts: the exact text sent. Returns its id.</summary>
    /// <param name="streamed">The reply arrives as tokens (the local model) rather than whole (a cloud provider).</param>
    public string Request(OutputPass pass, AiRequest request, bool streamed)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(request);
        lock (_order)
        {
            var id = NextId();
            _open[id] = new Open(_time.GetTimestamp());
            Write(new GenerationOutput(_jobId, id, RequestKind, pass.Step, pass.Title, AiRequestText.Render(request)) { Streamed = streamed });
            return id;
        }
    }

    /// <summary>Text that arrived for an open pass (coalesced before it is sent).</summary>
    /// <param name="elapsed">Time spent writing so far, as the model measured it.</param>
    public void Tokens(string passId, string delta, int outputTokens, TimeSpan? elapsed)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        lock (_order)
        {
            if (!_open.TryGetValue(passId, out var open))
            {
                return;
            }

            open.Text.Append(delta);
            var perSecond = elapsed is { TotalSeconds: > 0 } e && outputTokens > 1 ? Math.Round((outputTokens - 1) / e.TotalSeconds, 1) : (double?)null;
            Write(new GenerationOutput(_jobId, passId, TokenKind, null, null, delta)
            {
                OutputTokens = outputTokens,
                TokensPerSecond = perSecond,
                ElapsedMs = elapsed is { } w ? (long)w.TotalMilliseconds : null,
            });
        }
    }

    /// <summary>The local model finished a pass: its reply is the text streamed for it.</summary>
    public void Answered(string passId, int outputTokens, AiAnswerFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        lock (_order)
        {
            if (_open.Remove(passId, out var open))
            {
                Write(new GenerationOutput(_jobId, passId, ReplyKind, null, null, open.Text.ToString())
                {
                    OutputTokens = outputTokens,
                    PromptTokens = facts.PromptTokens,
                    TokensPerSecond = facts.TokensPerSecond > 0 ? facts.TokensPerSecond : null,
                    ElapsedMs = (long)_time.GetElapsedTime(open.Started).TotalMilliseconds,
                    StopReason = facts.StopReason,
                });
            }
        }
    }

    /// <summary>The whole reply (a cloud provider, or a local pass that was not answered on its own). Ignored for a pass that has its reply.</summary>
    public void Reply(string passId, AiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_order)
        {
            if (_open.Remove(passId, out var open))
            {
                var writing = response.Timings.Generation?.TotalSeconds ?? 0;
                Write(new GenerationOutput(_jobId, passId, ReplyKind, null, null, response.Text)
                {
                    OutputTokens = response.Usage.OutputTokens,
                    PromptTokens = response.Usage.InputTokens,
                    TokensPerSecond = response.ProviderId == AI.Local.LocalAiProvider.ProviderId && writing > 0 && response.Usage.OutputTokens > 1
                        ? Math.Round((response.Usage.OutputTokens - 1) / writing, 1)
                        : null,
                    ElapsedMs = (long)_time.GetElapsedTime(open.Started).TotalMilliseconds,
                    StopReason = response.ProviderStopReason ?? response.StopReason.ToString(),
                });
            }
        }
    }

    /// <summary>Ends the output after everything raised before it; nothing raised afterwards is sent.</summary>
    /// <returns>A task that completes once <c>done</c> has been published.</returns>
    public Task CompleteAsync()
    {
        lock (_order)
        {
            if (!_closed)
            {
                _closed = true;
                _open.Clear();
                _channel.Writer.TryWrite(new GenerationOutput(_jobId, string.Empty, DoneKind, null, null, string.Empty));
                _channel.Writer.TryComplete();
            }
        }

        return _consumer;
    }

    private string NextId() => "p" + (++_passes).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void Write(GenerationOutput item)
    {
        if (!_closed)
        {
            _channel.Writer.TryWrite(item);
        }
    }

    private async Task ConsumeAsync()
    {
        // Token text waiting to be sent, by pass, in the order the passes first had some.
        var waiting = new List<(string PassId, StringBuilder Text, GenerationOutput Last)>();
        long? lastTokens = null;

        void SendWaiting()
        {
            foreach (var (_, text, last) in waiting)
            {
                Publish(last with { Text = text.ToString() });
            }

            waiting.Clear();
            lastTokens = _time.GetTimestamp();
        }

        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item.Kind == TokenKind)
            {
                var index = waiting.FindIndex(w => w.PassId == item.PassId);
                if (index < 0)
                {
                    waiting.Add((item.PassId, new StringBuilder(item.Text), item));
                }
                else
                {
                    waiting[index].Text.Append(item.Text);
                    waiting[index] = waiting[index] with { Last = item };
                }

                if (lastTokens is not { } last || _time.GetElapsedTime(last) >= TokenInterval)
                {
                    SendWaiting();
                }

                continue;
            }

            if (waiting.Count > 0)
            {
                SendWaiting();
            }

            Publish(item);
            if (item.Kind == DoneKind)
            {
                return;
            }
        }
    }

    private void Publish(GenerationOutput item)
    {
        try
        {
            _publish(item);
        }
#pragma warning disable CA1031 // A failing sink must not stop the events that follow, above all the last one.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private sealed class Open(long started)
    {
        public long Started { get; } = started;

        public StringBuilder Text { get; } = new();
    }
}
