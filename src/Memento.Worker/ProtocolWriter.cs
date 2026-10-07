using System.Text;
using System.Text.Json;
using Memento.Core.Workers;

namespace Memento.Worker;

/// <summary>Writes one protocol line per reply to the original stdout, flushed, from any thread.</summary>
internal sealed class ProtocolWriter(Stream stream) : IDisposable
{
    private readonly StreamWriter _writer = new(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    private readonly object _gate = new();

    public void Send(WorkerReply reply)
    {
        var json = JsonSerializer.Serialize(reply, WorkerJsonContext.Default.WorkerReply);
        lock (_gate)
        {
            _writer.WriteLine(json);
        }
    }

    public void Log(string message) => Send(new WorkerReply { Type = WorkerMessageTypes.Log, Level = "info", Message = message });

    public void Dispose() => _writer.Dispose();
}
