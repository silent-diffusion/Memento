using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Memento.Video;
using Memento.Video.Gpu;
using Memento.Video.Sources;

namespace Memento.Tools.VideoCheck;

/// <summary>
/// window [--adapter amd] [--to-display 1]: what window capture does when the window is minimised, restored, moved to
/// another display, resized and closed (ENGINE-NOTES.md §M.1). A repainting test window is captured; frames per second,
/// the source's state changes and the loss are printed with their times.
/// </summary>
internal static class WindowCheck
{
    public static int Run(Options o) => RunAsync(o).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(Options o)
    {
        var displays = VideoSourceEnumerator.ListDisplays();
        var monitors = Memento.Video.Interop.User32.Monitors();
        Rectangle Bounds(int display)
        {
            var m = monitors.First(x => x.DeviceName == displays[display - 1].Id.Key).Bounds;
            return new Rectangle(m.Left + 100, m.Top + 100, 640, 400);
        }

        using var animator = new Animator(Bounds(o.Int("display", 1)));
        var form = animator.Form!;
        var hwnd = (IntPtr)form.Invoke(() => form.Handle);
        using var device = VideoDevice.Create(Checks.AdapterLuid(o));
        await using var source = GraphicsCaptureSource.ForWindow(device, VideoSourceId.Window(hwnd), "Test window");
        var clock = Stopwatch.StartNew();
        source.StateChanged += (_, e) => Console.WriteLine($"  {clock.Elapsed.TotalSeconds:0.00} s state {e.State} {e.Reason}");
        source.Lost += (_, e) => Console.WriteLine($"  {clock.Elapsed.TotalSeconds:0.00} s lost: {e.Error.Code}: {e.Error.Message}");
        await source.StartAsync(CancellationToken.None);

        (double At, string What, Action Do)[] steps =
        [
            (3, "minimise", () => form.WindowState = FormWindowState.Minimized),
            (6, "restore", () => form.WindowState = FormWindowState.Normal),
            (9, $"move to display {o.Int("to-display", 2)}", () => form.Bounds = Bounds(o.Int("to-display", 2))),
            (12, "resize to 900 × 300", () => form.Size = new Size(900, 300)),
            (15, "close", form.Close),
        ];
        long last = 0;
        var step = 0;
        for (var second = 1; second <= 17; second++)
        {
            while (step < steps.Length && clock.Elapsed.TotalSeconds >= steps[step].At)
            {
                Console.WriteLine($"  {clock.Elapsed.TotalSeconds:0.00} s {steps[step].What}");
                try
                {
                    form.Invoke(steps[step].Do);
                }
                catch (InvalidOperationException)
                {
                }

                step++;
            }

            await Task.Delay(TimeSpan.FromSeconds(second) - clock.Elapsed);
            var stats = source.Statistics;
            Console.WriteLine($"{second,3} s: {stats.FramesArrived - last} frames, picture {stats.Width}x{stats.Height}, state {source.State}");
            last = stats.FramesArrived;
        }

        return source.Loss is null ? 1 : 0;
    }
}
