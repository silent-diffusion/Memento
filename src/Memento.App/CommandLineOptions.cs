using System.IO;

namespace Memento.App;

/// <summary>
/// Command-line switches for reviewers and CI:
/// <c>--screenshot &lt;path&gt;</c> captures the page to a PNG once the UI reports ready, then exits 0;
/// <c>--theme light|dark</c> forces the theme for this run only (nothing is saved).
/// </summary>
internal sealed record CommandLineOptions(string? ScreenshotPath, string? ForcedTheme)
{
    public static CommandLineOptions Default { get; } = new(null, null);

    public bool IsScreenshotRun => ScreenshotPath is not null;

    /// <exception cref="ArgumentException">A switch is missing its value or has an unsupported one.</exception>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? screenshot = null;
        string? theme = null;
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
                default:
                    // Unknown switches are ignored (Velopack and Windows may pass their own).
                    break;
            }
        }

        return new CommandLineOptions(screenshot, theme);
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
