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
//   TranscriptionCheck diarize <audio> --segmentation <onnx> --embedding <onnx> [--threshold 0.8] [--count n] --out <json>
//                                                     run the worker's speaker job on one file and save its turns and voices
//   TranscriptionCheck identify <recordingId> [--count n]
//                                                     identify a library recording's speakers again through the real
//                                                     stage (with its own count when given) and print the result
//   TranscriptionCheck evaluate <diarize.json> --transcript <transcript.json> [--reference <transcript.json>]
//                     [--counts auto,4] [--join none,0.7]
//                                                     assign a saved speaker job to a transcript's lines with each count and
//                                                     join similarity, and score it against the reference's renamed speakers
//   TranscriptionCheck voicematch <diarize.json> --transcript <named transcript.json> [--b <diarize.json> --b-transcript <json>] [--piece 30]
//                                                     the known-voices threshold study (ENGINE-NOTES.md §N): genuine and
//                                                     impostor tries against people enrolled from the named transcript
//   TranscriptionCheck chapters <transcript.json> [--annotations <annotations.json>] [--titles]
//                                                     suggested chapters for a transcript and its topics (titles on request)
//   TranscriptionCheck voicepair <diarize.json> <diarize.json>
//                                                     the cosine of every voice of one saved speaker job with the other's
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
    case "identify":
        return await check.IdentifyAsync(args[1], Option("--count") is { } count ? int.Parse(count, CultureInfo.InvariantCulture) : null);
    case "evaluate":
        return Evaluation.Run(
            args[1],
            Option("--transcript") ?? throw new ArgumentException("--transcript <transcript.json> is needed"),
            Option("--reference"),
            (Option("--counts") ?? "auto").Split(','),
            (Option("--join") ?? "none").Split(','),
            (Option("--min") ?? "0").Split(','),
            (Option("--fold") ?? "never").Split(','),
            double.Parse(Option("--own") ?? "0", CultureInfo.InvariantCulture));
    case "diarize":
        return await check.DiarizeAsync(
            args[1],
            Option("--segmentation") ?? throw new ArgumentException("--segmentation <onnx> is needed"),
            Option("--embedding") ?? throw new ArgumentException("--embedding <onnx> is needed"),
            float.Parse(Option("--threshold") ?? "0.8", CultureInfo.InvariantCulture),
            int.Parse(Option("--count") ?? "-1", CultureInfo.InvariantCulture),
            Option("--out") ?? throw new ArgumentException("--out <json> is needed"));
    case "chapters":
        return VoiceStudy.Chapters(args[1], Option("--annotations"), args.Contains("--titles"));
    case "voicepair":
        return VoiceStudy.Pair(args[1], args[2]);
    case "voicematch":
        return VoiceStudy.Run(
            args[1],
            Option("--transcript") ?? throw new ArgumentException("--transcript <transcript.json> is needed (its named speakers are the reference)"),
            Option("--b"),
            Option("--b-transcript"),
            double.Parse(Option("--piece") ?? "30", CultureInfo.InvariantCulture));
    default:
        Console.WriteLine("usage: TranscriptionCheck models | install <id>... | adopt <id> <file> | run <wav> [--model id] [--cpu] | killtest <wav> | show <id>   [--worker <exe>]");
        return 2;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
