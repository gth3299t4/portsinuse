using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PortsInUse
{
    /// <summary>
    /// Vsync-paced frame clock. A background thread waits on DwmFlush (returns once per display
    /// refresh), then asks the UI thread to run one frame. Only one frame is ever queued, so if
    /// painting is slow the rate drops gracefully instead of lagging behind.
    /// </summary>
    public static class Animator
    {
        [DllImport("dwmapi.dll")] static extern int DwmFlush();

        static readonly Stopwatch clock = Stopwatch.StartNew();
        static Control host;
        static volatile bool enabled;
        static int pending;
        static double lastFrame, fpsWindowStart;
        static int fpsFrames;
        static double frameMsAccum;

        /// <summary>Raised on the UI thread once per displayed frame. Argument: seconds since previous frame.</summary>
        public static event Action<double> Frame;

        public static double Now { get { return clock.Elapsed.TotalSeconds; } }
        public static bool Active { get { return enabled; } }
        public static double Fps { get; private set; }
        public static double FrameMs { get; private set; }

        public static void Start(Control uiHost)
        {
            host = uiHost;
            var t = new Thread(Loop) { IsBackground = true, Name = "Vsync frame clock" };
            t.Start();
        }

        public static void SetEnabled(bool on)
        {
            if (on && !enabled) lastFrame = Now;
            enabled = on;
            if (!on) { Fps = 0; FrameMs = 0; }
        }

        static void Loop()
        {
            while (true)
            {
                if (!enabled || host == null || !host.IsHandleCreated) { Thread.Sleep(100); continue; }
                if (DwmFlush() != 0) Thread.Sleep(16);   // fallback pacing if DWM is unavailable
                if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) continue;  // previous frame still painting
                try { host.BeginInvoke((Action)RunFrame); }
                catch { pending = 0; }
            }
        }

        static void RunFrame()
        {
            double start = Now;
            try
            {
                if (enabled && Frame != null)
                {
                    double dt = Math.Min(0.25, start - lastFrame);
                    Frame(dt);
                }
            }
            catch { }
            finally
            {
                lastFrame = start;
                double end = Now;
                frameMsAccum += (end - start) * 1000;
                fpsFrames++;
                if (end - fpsWindowStart >= 1.0)
                {
                    Fps = fpsFrames / (end - fpsWindowStart);
                    FrameMs = frameMsAccum / Math.Max(1, fpsFrames);
                    fpsFrames = 0; frameMsAccum = 0; fpsWindowStart = end;
                }
                Interlocked.Exchange(ref pending, 0);
            }
        }

        /// <summary>Invalidate and paint synchronously, so frame cost is measured and frames never pile up.</summary>
        public static void Repaint(Control c)
        {
            if (!c.Visible || c.Width <= 0 || c.Height <= 0) return;
            c.Invalidate();
            c.Update();
        }

        public static void Repaint(Control c, Rectangle r)
        {
            if (!c.Visible) return;
            c.Invalidate(r);
            c.Update();
        }
    }
}
