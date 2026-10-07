using System.Text.Json;
using System.Threading.Channels;
using Memento.Core.Workers;

namespace Memento.Core.Tests.Fakes;

/// <summary>
/// An in-process stand-in for Memento.Worker.exe: each start runs <see cref="Script"/>, which answers the job with
/// protocol lines (or raw text, or nothing) and returns an exit code. A <c>cancel</c> line cancels the script's token.
/// </summary>
internal sealed class ScriptedWorkerLauncher : IWorkerLauncher
{
    private readonly object _gate = new();
    private readonly List<ScriptedWorkerProcess> _started = [];

    /// <summary>The worker's behaviour: reply through the context, return the exit code.</summary>
    public Func<WorkerJob, ScriptedWorkerContext, CancellationToken, Task<int>> Script { get; set; } =
        (_, context, _) =>
        {
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return Task.FromResult(0);
        };

    /// <summary>Throw this from <see cref="Start"/> (a missing worker).</summary>
    public Exception? StartFailure { get; set; }

    public List<ScriptedWorkerProcess> Started
    {
        get
        {
            lock (_gate)
            {
                return [.. _started];
            }
        }
    }

    public IWorkerProcess Start()
    {
        if (StartFailure is { } failure)
        {
            throw failure;
        }

        var process = new ScriptedWorkerProcess(Script);
        lock (_gate)
        {
            _started.Add(process);
        }

        return process;
    }
}

/// <summary>What a script can do: send lines and see the job and cancel requests.</summary>
internal sealed class ScriptedWorkerContext(ChannelWriter<string?> output, ChannelReader<WorkerCommand> commands)
{
    /// <summary>The host's lines after <c>start</c>, other than <c>cancel</c> (a session's <c>prompts</c> and <c>end</c>).</summary>
    public ChannelReader<WorkerCommand> Commands => commands;

    public void Send(WorkerReply reply) => output.TryWrite(JsonSerializer.Serialize(reply, WorkerJsonContext.Default.WorkerReply));

    public void SendRaw(string line) => output.TryWrite(line);
}

internal sealed class ScriptedWorkerProcess : IWorkerProcess
{
    private static int _nextId = 9000;
    private readonly Channel<string?> _output = Channel.CreateUnbounded<string?>();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancel = new();
    private readonly Func<WorkerJob, ScriptedWorkerContext, CancellationToken, Task<int>> _script;
    private readonly List<string> _received = [];
    private readonly Channel<WorkerCommand> _commands = Channel.CreateUnbounded<WorkerCommand>();

    public ScriptedWorkerProcess(Func<WorkerJob, ScriptedWorkerContext, CancellationToken, Task<int>> script)
    {
        _script = script;
        Id = Interlocked.Increment(ref _nextId);
        Input = new LineWriter(OnLine);
        Output = new ChannelReaderText(_output.Reader);
    }

    public int Id { get; }

    public TextWriter Input { get; }

    public TextReader Output { get; }

    public Task<int> Exited => _exited.Task;

    public TimeSpan CpuTime => TimeSpan.Zero;

    public IReadOnlyList<string> ErrorTail { get; set; } = [];

    public bool Killed { get; private set; }

    public bool CancelReceived => _cancel.IsCancellationRequested;

    public List<string> Received
    {
        get
        {
            lock (_received)
            {
                return [.. _received];
            }
        }
    }

    public WorkerJob? Job { get; private set; }

    public void Kill()
    {
        Killed = true;
        Exit(unchecked((int)0xC0000409));
    }

    public void Dispose() => _cancel.Dispose();

    private void Exit(int code)
    {
        _output.Writer.TryComplete();
        _exited.TrySetResult(code);
    }

    private void OnLine(string line)
    {
        lock (_received)
        {
            _received.Add(line);
        }

        var command = JsonSerializer.Deserialize(line, WorkerJsonContext.Default.WorkerCommand)!;
        if (command.Type == WorkerMessageTypes.Start)
        {
            Job = command.Job;
            var context = new ScriptedWorkerContext(_output.Writer, _commands.Reader);
            _ = Task.Run(async () =>
            {
                int code;
                try
                {
                    code = await _script(command.Job!, context, _cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    context.Send(new WorkerReply { Type = WorkerMessageTypes.Cancelled });
                    code = 3;
                }

                Exit(code);
            });
        }
        else if (command.Type == WorkerMessageTypes.Cancel)
        {
            _cancel.Cancel();
        }
        else
        {
            _commands.Writer.TryWrite(command);
        }
    }

    private sealed class LineWriter(Action<string> onLine) : TextWriter
    {
        private readonly System.Text.StringBuilder _pending = new();

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                var line = _pending.ToString().TrimEnd('\r');
                _pending.Clear();
                onLine(line);
            }
            else
            {
                _pending.Append(value);
            }
        }

        public override Task WriteLineAsync(string? value)
        {
            onLine(value ?? string.Empty);
            return Task.CompletedTask;
        }

        public override Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class ChannelReaderText(ChannelReader<string?> reader) : TextReader
    {
        public override string? ReadLine() => ReadLineAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                if (reader.TryRead(out var line))
                {
                    return line;
                }
            }

            return null;
        }
    }
}
