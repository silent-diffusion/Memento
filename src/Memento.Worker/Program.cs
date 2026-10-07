using System.Text;
using System.Text.Json;
using Memento.Core.Workers;
using Memento.Worker;

// Memento.Worker.exe: reads one "start" line from stdin, runs the job (transcribe, diarize or llm), writes protocol
// lines to stdout and exits.
// A "cancel" line (or stdin closing because the app went away) stops the job. Native libraries may print to the
// process's stdout, so the protocol keeps the original stdout handle and everything else is sent to stderr.
var protocolStream = Console.OpenStandardOutput();
NativeConsole.RedirectStdoutToStderr();
Console.SetOut(Console.Error);

using var output = new ProtocolWriter(protocolStream);
using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
output.Send(new WorkerReply { Type = WorkerMessageTypes.Ready, Pid = Environment.ProcessId });

WorkerCommand? command;
try
{
    var line = await input.ReadLineAsync();
    command = line is null ? null : JsonSerializer.Deserialize(line, WorkerJsonContext.Default.WorkerCommand);
}
catch (JsonException ex)
{
    output.Send(new WorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.InvalidJob, Message = "The job could not be read: " + ex.Message });
    return 2;
}

if (command is not { Type: WorkerMessageTypes.Start, Job: { } job })
{
    output.Send(new WorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.InvalidJob, Message = "The first line must be a start command with a job." });
    return 2;
}

using var cancel = new CancellationTokenSource();
_ = Task.Run(async () =>
{
    // Further lines: "cancel". End of input means the app has gone; stop as well.
    try
    {
        while (await input.ReadLineAsync() is { } next)
        {
            if (next.Contains("\"cancel\"", StringComparison.Ordinal))
            {
                break;
            }
        }
    }
    catch (IOException)
    {
        // The pipe broke: the app has gone.
    }

    await cancel.CancelAsync();
});

try
{
    var result = job.Kind switch
    {
        WorkerJobKinds.Transcribe when job.Transcribe is { } transcribe => new WorkerReply
        {
            Type = WorkerMessageTypes.Result,
            Transcription = await new WhisperTranscriber(output).RunAsync(transcribe, cancel.Token),
        },
        WorkerJobKinds.Diarize when job.Diarize is { } diarize => new WorkerReply
        {
            Type = WorkerMessageTypes.Result,
            Diarization = new SherpaDiarizer(output).Run(diarize, cancel.Token),
        },
        WorkerJobKinds.Llm when job.Llm is { } llm => await new LlmJob(output).RunAsync(llm, cancel.Token),
        _ => throw new WorkerFailure(WorkerErrorCodes.InvalidJob, $"Job kind '{job.Kind}' has no body or is not known."),
    };
    output.Send(result);
    return 0;
}
catch (OperationCanceledException)
{
    output.Send(new WorkerReply { Type = WorkerMessageTypes.Cancelled });
    return 3;
}
catch (WorkerFailure failure)
{
    output.Send(new WorkerReply { Type = WorkerMessageTypes.Error, Code = failure.Code, Message = failure.Message });
    return 1;
}
catch (OutOfMemoryException)
{
    output.Send(new WorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.OutOfMemory, Message = "There was not enough memory." });
    return 1;
}
#pragma warning disable CA1031 // Every failure becomes a structured error line before the process exits.
catch (Exception ex)
#pragma warning restore CA1031
{
    Console.Error.WriteLine(ex.ToString());
    output.Send(new WorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.Engine, Message = ex.Message });
    return 1;
}
