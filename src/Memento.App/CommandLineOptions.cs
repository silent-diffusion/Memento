using System.Globalization;
using System.IO;
using Memento.Core.Recording.Simulation;

namespace Memento.App;

/// <summary>
/// Command-line switches for reviewers and CI:
/// <c>--screenshot &lt;path&gt;</c> captures the page to a PNG once the UI reports ready, then exits 0;
/// <c>--theme light|dark</c> forces the theme for this run only (nothing is saved);
/// <c>--simulate-audio [lose-source=&lt;s&gt;,disk-full=&lt;s&gt;]</c> (hidden) records from the simulated engine,
/// optionally unplugging the system-audio source or filling the disk after that many seconds of a session.
/// </summary>
internal sealed record CommandLineOptions(string? ScreenshotPath, string? ForcedTheme, SimulatedEngineOptions? SimulateAudio)
{
    public static CommandLineOptions Default { get; } = new(null, null, null);

    public bool IsScreenshotRun => ScreenshotPath is not null;

    /// <exception cref="ArgumentException">A switch is missing its value or has an unsupported one.</exception>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? screenshot = null;
        string? theme = null;
        SimulatedEngineOptions? simulate = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--screenshot":
                    screenshot = Path.GetFullPath(ValueAfter(args, ref i, "--screenshot needs a PNG file path."));
                    break;
                case "--theme":
                    theme = ValueAfter(args, ref i, "--theme needs 'light' or 'dark'.").ToLowerInvariant();
                    if (theme is not ("light" or "dark"))
                    {
                        throw new ArgumentException($"--theme accepts 'light' or 'dark', not '{theme}'.", nameof(args));
                    }

                    break;
                case "--simulate-audio":
                    simulate = new SimulatedEngineOptions();
                    if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        simulate = ParseSimulation(args[++i]);
                    }

                    break;
                default:
                    // Unknown switches are ignored (Velopack and Windows may pass their own).
                    break;
            }
        }

        return new CommandLineOptions(screenshot, theme, simulate);
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
