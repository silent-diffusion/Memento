using System.Globalization;

namespace Memento.Video.Sources;

/// <summary>
/// Names displays the way the picker shows them: the primary display is "Display 1", the others are numbered left to
/// right (then top to bottom), and each gets a hint relative to Display 1 ("left of Display 1"). Pure, for tests.
/// </summary>
public static class DisplayNaming
{
    /// <summary>A display as Windows reports it: GDI device name, desktop bounds in physical pixels, primary flag.</summary>
    public sealed record DisplayEntry(string DeviceName, int Left, int Top, int Width, int Height, bool IsPrimary, int RefreshRate);

    public static IReadOnlyList<VideoSourceInfo> Name(IReadOnlyList<DisplayEntry> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? (displays.Count > 0 ? displays[0] : null);
        var ordered = displays
            .OrderByDescending(d => ReferenceEquals(d, primary))
            .ThenBy(d => d.Left)
            .ThenBy(d => d.Top)
            .ToList();
        var result = new List<VideoSourceInfo>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var d = ordered[i];
            var name = string.Create(CultureInfo.InvariantCulture, $"Display {i + 1}");
            var parts = new List<string> { string.Create(CultureInfo.InvariantCulture, $"{d.Width} × {d.Height}") };
            if (d.RefreshRate > 1)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{d.RefreshRate} Hz"));
            }

            parts.Add(ReferenceEquals(d, primary) ? "primary" : Relative(d, primary!));
            result.Add(new VideoSourceInfo(VideoSourceId.Display(d.DeviceName), name, string.Join(" · ", parts), d.Width, d.Height, d.RefreshRate, ReferenceEquals(d, primary), false));
        }

        return result;
    }

    private static string Relative(DisplayEntry d, DisplayEntry primary)
    {
        if (d.Left >= primary.Left + primary.Width)
        {
            return "right of Display 1";
        }

        if (d.Left + d.Width <= primary.Left)
        {
            return "left of Display 1";
        }

        return d.Top + d.Height <= primary.Top ? "above Display 1" : "below Display 1";
    }
}
