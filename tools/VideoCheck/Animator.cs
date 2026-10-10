using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Memento.Tools.VideoCheck;

/// <summary>
/// A window that repaints continuously on a chosen display (a moving block and a running counter), so the display has
/// something to capture at its full refresh rate. Runs on its own STA thread; dispose to close it.
/// </summary>
internal sealed class Animator : IDisposable
{
    private readonly Thread _thread;
    private AnimatedForm? _form;

    /// <summary>Paints so far.</summary>
    public long Frames => _form?.Frames ?? 0;

    /// <summary>The window, for checks that move or close it (call it through Invoke).</summary>
    public Form? Form => _form;

    public Animator(Rectangle bounds)
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _form = new AnimatedForm(bounds);
            _form.Shown += (_, _) => ready.Set();
            Application.Run(_form);
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
        ready.Wait(5000);
    }

    public void Dispose()
    {
        try
        {
            _form?.Invoke(() => _form.Close());
        }
        catch (InvalidOperationException)
        {
        }

        _thread.Join(2000);
    }

    private sealed class AnimatedForm : Form
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Font _font = new("Consolas", 48);
        public long Frames => _frames;

        private long _frames;

        public AnimatedForm(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(30, 40, 60);
            Text = "VideoCheck animation";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = _clock.Elapsed.TotalSeconds;
            var x = (int)((Math.Sin(t * 2) + 1) / 2 * (ClientSize.Width - 200));
            e.Graphics.FillRectangle(Brushes.Orange, x, ClientSize.Height / 3, 200, 160);
            e.Graphics.DrawString($"{t:0.000} s  frame {_frames++}", _font, Brushes.White, 40, 40);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
