using System.IO.Pipes;
using System.Text;
using Memento.AI.Local;

namespace Memento.AI.Tests.Fakes;

/// <summary>
/// A worker on the other end of two in-process pipes: the host gets a <see cref="LocalLlmChannel"/>, the worker side
/// runs a script (normally <see cref="LocalLlmJobRunner.RunJsonLinesAsync"/>) and closes its output when it ends,
/// exactly like a worker process exiting.
/// </summary>
internal sealed class PipeWorker : IAsyncDisposable
{
    private readonly AnonymousPipeServerStream _toWorker = new(PipeDirection.Out);
    private readonly AnonymousPipeClientStream _workerIn;
    private readonly AnonymousPipeServerStream _fromWorker = new(PipeDirection.Out);
    private readonly AnonymousPipeClientStream _hostIn;

    public PipeWorker()
    {
        _workerIn = new AnonymousPipeClientStream(PipeDirection.In, _toWorker.ClientSafePipeHandle);
        _hostIn = new AnonymousPipeClientStream(PipeDirection.In, _fromWorker.ClientSafePipeHandle);
    }

    public Task<int>? Worker { get; private set; }

    public List<string> HostSent { get; } = [];

    /// <summary>Starts the worker side and returns the host's channel.</summary>
    public LocalLlmChannel Start(Func<TextReader, TextWriter, Task<int>> script)
    {
        var workerReader = new StreamReader(_workerIn, new UTF8Encoding(false));
        var workerWriter = new StreamWriter(_fromWorker, new UTF8Encoding(false)) { AutoFlush = true };
        Worker = Task.Run(async () =>
        {
            try
            {
                return await script(workerReader, workerWriter);
            }
            finally
            {
                await workerWriter.DisposeAsync();
            }
        });
        var hostWriter = new RecordingWriter(new StreamWriter(_toWorker, new UTF8Encoding(false)) { AutoFlush = true }, HostSent);
        return new LocalLlmChannel(hostWriter, new StreamReader(_hostIn, new UTF8Encoding(false)), this);
    }

    public async ValueTask DisposeAsync()
    {
        await _toWorker.DisposeAsync();
        if (Worker is not null)
        {
            await Task.WhenAny(Worker, Task.Delay(TimeSpan.FromSeconds(5)));
        }

        await _workerIn.DisposeAsync();
        await _fromWorker.DisposeAsync();
        await _hostIn.DisposeAsync();
    }

    /// <summary>Keeps every line the host wrote.</summary>
    private sealed class RecordingWriter(TextWriter inner, List<string> lines) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value) => inner.Write(value);

        public override Task WriteAsync(string? value)
        {
            lock (lines)
            {
                lines.Add(value ?? string.Empty);
            }

            return inner.WriteAsync(value);
        }

        public override Task FlushAsync() => inner.FlushAsync();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
