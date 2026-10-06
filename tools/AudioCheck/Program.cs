// Manual end-to-end check of Memento.Audio on real devices. Writes only under %TEMP%\memento-audiocheck (or --out)
// and deletes every file containing microphone audio before it exits.
//
//   AudioCheck sources                         list what can be recorded
//   AudioCheck record [--seconds 60] [--checkpoint 10]
//                                              mic + system loopback + one app (a child PowerShell playing a tone),
//                                              then FLAC, mixdown, peaks; prints formats, frames vs clock, drift, sizes
//   AudioCheck killtest [--after 25]           records 3 sources in a child process, kills it after a checkpoint,
//                                              repairs the WAVs and prints the recovered duration per track
using System.Globalization;
using Memento.Audio.Sources;
using Memento.Tools.AudioCheck;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var command = args.Length > 0 ? args[0] : "help";
var outDir = Option("--out") ?? Path.Combine(Path.GetTempPath(), "memento-audiocheck", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

switch (command)
{
    case "sources":
        foreach (var s in new AudioSourceEnumerator().List(new AudioSourceListOptions { IncludeIcons = true }))
        {
            Console.WriteLine($"{s.Kind,-12} {(s.IsDefault ? "*" : " ")} {s.Name} | {s.Detail} | icon {(s.IconPng is null ? "-" : s.IconPng.Length + " B PNG")}");
        }

        return 0;
    case "record":
        return await Checks.RecordAsync(outDir, IntOption("--seconds", 60), IntOption("--checkpoint", 10));
    case "killtest":
        return await Checks.KillTestAsync(outDir, IntOption("--after", 25));
    case "kill-child":
        return await Checks.KillChildAsync(outDir, Option("--mic")!, Option("--system")!, int.Parse(Option("--app")!, CultureInfo.InvariantCulture), IntOption("--checkpoint", 10));
    default:
        Console.WriteLine("usage: AudioCheck sources | record [--seconds 60] [--checkpoint 10] | killtest [--after 25]   [--out <dir>]");
        return 2;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

int IntOption(string name, int fallback) => Option(name) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
