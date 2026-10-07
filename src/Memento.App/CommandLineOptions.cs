using System.Globalization;
using System.IO;
using Memento.Core.Recording.Simulation;

namespace Memento.App;

/// <summary>
/// Command-line switches for reviewers and CI:
/// <c>--screenshot &lt;path&gt;</c> captures the page to a PNG once the UI reports ready, then exits 0;
/// <c>--theme light|dark</c> forces the theme for this run only (nothing is saved);
/// <c>--simulate-audio [lose-source=&lt;s&gt;,disk-full=&lt;s&gt;]</c> (hidden) records from the simulated engine,
/// optionally unplugging the system-audio source or filling the disk after that many seconds of a session;
/// <c>--free-space-override=&lt;bytes|file&gt;</c> (hidden, tests) makes every drive report that many free bytes, or the
/// number written in that file, read again on every check;
/// <c>--rollover-bytes=&lt;n&gt;</c> (hidden, tests) rolls capture tracks over to their next <c>.partN.wav</c> at
/// <c>n</c> bytes instead of 3.5 GiB;
/// <c>--update-feed=&lt;url|folder|off&gt;</c> (hidden, tests) checks a local Velopack feed instead of the GitHub
/// releases, or turns update checks off for the run.
/// </summary>
internal sealed record CommandLineOptions(
    string? ScreenshotPath,
    string? ForcedTheme,
    SimulatedEngineOptions? SimulateAudio,
    string? FreeSpaceOverride = null,
    long? RolloverBytes = null,
    string? UpdateFeed = null)
{
    /// <summary>Smallest rollover a test may ask for: one second of int24 stereo at 48 kHz.</summary>
    public const long MinRolloverBytes = 288_000;

    public static CommandLineOptions Default { get; } = new(null, null, null);

    public bool IsScreenshotRun => ScreenshotPath is not null;

    /// <exception cref="ArgumentException">A switch is missing its value or has an unsupported one.</exception>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? screenshot = null;
        string? theme = null;
        SimulatedEngineOptions? simulate = null;
        string? freeSpace = null;
        long? rollover = null;
        string? updateFeed = null;
        for (var i = 0; i < args.Count; i++)
        {
            var (name, inline) = Split(args[i]);
            switch (name)
            {
                case "--screenshot":
                    screenshot = Path.GetFullPath(inline ?? ValueAfter(args, ref i, "--screenshot needs a PNG file path."));
                    break;
                case "--theme":
                    theme = (inline ?? ValueAfter(args, ref i, "--theme needs 'light' or 'dark'.")).ToLowerInvariant();
                    if (theme is not ("light" or "dark"))
                    {
                        throw new ArgumentException($"--theme accepts 'light' or 'dark', not '{theme}'.", nameof(args));
                    }

                    break;
                case "--simulate-audio":
                    simulate = new SimulatedEngineOptions();
                    if (inline is not null)
                    {
                        simulate = ParseSimulation(inline);
                    }
                    else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        simulate = ParseSimulation(args[++i]);
                    }

                    break;
                case "--free-space-override":
                    freeSpace = inline ?? ValueAfter(args, ref i, "--free-space-override needs a number of bytes or a file that holds one.");
                    break;
                case "--rollover-bytes":
                    var text = inline ?? ValueAfter(args, ref i, "--rollover-bytes needs a number of bytes.");
                    if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) || bytes < MinRolloverBytes)
                    {
                        throw new ArgumentException($"--rollover-bytes takes a whole number of at least {MinRolloverBytes}, not '{text}'.", nameof(args));
                    }

                    rollover = bytes;
                    break;
                case "--update-feed":
                    updateFeed = inline ?? ValueAfter(args, ref i, "--update-feed needs a feed URL, a folder, or 'off'.");
                    break;
                default:
                    // Unknown switches are ignored (Velopack and Windows may pass their own).
                    break;
            }
        }

        return new CommandLineOptions(screenshot, theme, simulate, freeSpace, rollover, updateFeed);
    }

    /// <summary><c>--name=value</c> → (<c>--name</c>, <c>value</c>); anything else → (argument, null).</summary>
    private static (string Name, string? Value) Split(string argument)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return (argument, null);
        }

        var equals = argument.IndexOf('=', StringComparison.Ordinal);
        return equals < 0 ? (argument, null) : (argument[..equals], argument[(equals + 1)..]);
    }

    private static SimulatedEngineOptions ParseSimulation(string spec)
    {
        var options = new SimulatedEngineOptions();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
            {
                throw new ArgumentException($"--simulate-audio takes lose-source=<seconds>,disk-full=<seconds>, not '{part}'.", nameof(spec));
            }

            options = pair[0] switch
            {
                "lose-source" => options with { LoseSourceAfter = TimeSpan.FromSeconds(seconds) },
                "disk-full" => options with { DiskFullAfter = TimeSpan.FromSeconds(seconds) },
                _ => throw new ArgumentException($"--simulate-audio does not know '{pair[0]}'; use lose-source or disk-full.", nameof(spec)),
            };
        }

        return options;
    }

    private static string ValueAfter(IReadOnlyList<string> args, ref int index, string error)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(error, nameof(args));
        }

        index++;
        return args[index];
    }
}
