// Manual end-to-end check of M2 transcription and speakers on real models and hardware.
// Everything lives under %LOCALAPPDATA%\Memento: point LOCALAPPDATA at a scratch folder first.
//
//   TranscriptionCheck models                         catalog and what is installed
//   TranscriptionCheck install <modelId>...           install through the model manager (times and verified hashes)
//   TranscriptionCheck adopt <modelId> <file>         copy a model already on disk after checking its SHA-256
//   TranscriptionCheck run <wav> [--model id] [--cpu] [--title t]
//                                                     import the WAV as a recording and run transcript, speakers, topics
//   TranscriptionCheck killtest <wav> [--model id]    kill the worker mid-pass, show the failure, retry on CPU
//   TranscriptionCheck show <recordingId>             print a recording's transcript summary and History
using System.Globalization;
using Memento.Tools.TranscriptionCheck;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var command = args.Length > 0 ? args[0] : "help";
await using var check = await Check.CreateAsync(Option("--worker"));
switch (command)
{
    case "models":
        check.ListModels();
        return 0;
    case "install":
        return await check.InstallAsync(args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList());
    case "adopt":
        return await check.AdoptAsync(args[1], args[2]);
    case "run":
        return await check.RunAsync(args[1], Option("--model"), args.Contains("--cpu"), Option("--title") ?? Path.GetFileNameWithoutExtension(args[1]));
    case "killtest":
        return await check.KillTestAsync(args[1], Option("--model"));
    case "show":
        return await check.ShowAsync(args[1]);
    default:
        Console.WriteLine("usage: TranscriptionCheck models | install <id>... | adopt <id> <file> | run <wav> [--model id] [--cpu] | killtest <wav> | show <id>   [--worker <exe>]");
        return 2;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
