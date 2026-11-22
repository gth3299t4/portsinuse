using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PortsInUse
{
    public class MapNode
    {
        public string Title = "", Sub = "", Detail = "", Scope = "";
        public bool Active;
    }

    public class MapData
    {
        public string Center = "", CenterSub = "", CenterDetail = "";
        public List<MapNode> Inbound = new List<MapNode>();
        public List<MapNode> Outbound = new List<MapNode>();
    }

    /// <summary>Application-dependency style map: inbound clients on the left, the app in the middle, outbound destinations on the right.</summary>
    public class MapPanel : Panel
    {
        MapData data = new MapData();

        class MapLayout
        {
            public float W, H, Margin, Header;
            public RectangleF Center;
            public List<RectangleF> In = new List<RectangleF>(), Out = new List<RectangleF>();
        }

        public MapPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Theme.Bg;
            HandleCreated += (s, e) => Theme.DarkScrollbars(this);
        }

        public void SetData(MapData d)
        {
            data = d ?? new MapData();
            Size req;
            using (var g = CreateGraphics())
            {
                var L = DoLayout(g, Math.Max(1, ClientSize.Width - SystemInformation.VerticalScrollBarWidth), 0, 0);
                req = new Size((int)Math.Ceiling(L.W), (int)Math.Ceiling(L.H));
            }
            if (AutoScrollMinSize != req) AutoScrollMinSize = req;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated) SetData(data);
        }

        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

        MapLayout DoLayout(Graphics g, float viewW, float viewH, float scrollY)
        {
            float s = Theme.Scale;
            var L = new MapLayout();
            float nodeW = 245 * s, nodeH = 58 * s, gap = 10 * s, colGap = 90 * s, centerW = 270 * s, pad = 12 * s;
            L.Margin = 18 * s;
            L.Header = 36 * s;
            if (viewW > 0)
            {
                // squeeze columns to fit the visible width where possible
                float room = viewW - 2 * L.Margin - centerW;
                nodeW = Math.Min(245 * s, Math.Max(180 * s, (room - 120 * s) / 2));
                colGap = Math.Max(50 * s, Math.Min(90 * s, (room - 2 * nodeW) / 2));
            }
            float tw = centerW - 2 * pad;
            float ch = pad * 2 + g.MeasureString(data.Center, Theme.Title, (int)tw).Height;
            if (data.CenterSub.Length > 0) ch += g.MeasureString(data.CenterSub, Theme.Small, (int)tw).Height + 4 * s;
            if (data.CenterDetail.Length > 0) ch += g.MeasureString(data.CenterDetail, Theme.Mono, (int)tw).Height + 8 * s;

            int rows = Math.Max(data.Inbound.Count, data.Outbound.Count);
            float colH = rows * nodeH + Math.Max(0, rows - 1) * gap;
            float minW = L.Margin * 2 + nodeW * 2 + colGap * 2 + centerW;
            float minH = L.Margin * 2 + L.Header + Math.Max(colH, ch + 60 * s);
            L.W = Math.Max(viewW, minW);
            L.H = Math.Max(viewH, minH);

            float top = L.Margin + L.Header;
            float avail = L.H - top - L.Margin;
            float visH = viewH > 0 ? viewH : L.H;
            float cy = scrollY + (visH - ch) / 2;
            cy = Math.Max(top, Math.Min(cy, L.H - L.Margin - ch - 40 * s));
            L.Center = new RectangleF((L.W - centerW) / 2, cy, centerW, ch);

            // short column: keep it level with the (sticky) centre node; tall column: start at the top
            float centerMid = L.Center.Y + ch / 2;
            Func<int, float> y0 = n =>
            {
                float h = n * nodeH + Math.Max(0, n - 1) * gap;
                if (h >= avail) return top;
                return Math.Max(top, Math.Min(centerMid - h / 2, L.H - L.Margin - h));
            };
            float yi = y0(data.Inbound.Count);
            for (int i = 0; i < data.Inbound.Count; i++) L.In.Add(new RectangleF(L.Margin, yi + i * (nodeH + gap), nodeW, nodeH));
            float yo = y0(data.Outbound.Count);
            for (int i = 0; i < data.Outbound.Count; i++) L.Out.Add(new RectangleF(L.W - L.Margin - nodeW, yo + i * (nodeH + gap), nodeW, nodeH));
            return L;
        }

        /// <summary>Milliseconds the last full paint took (used to throttle the animation on big maps).</summary>
        public double LastPaintMs;

        /// <summary>True when at least one link is live, i.e. there is something to animate.</summary>
        public bool HasActiveLinks
        {
            get { return data.Inbound.Exists(n => n.Active) || data.Outbound.Exists(n => n.Active); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            PaintMap(e);
            LastPaintMs = sw.Elapsed.TotalMilliseconds;
        }

        void PaintMap(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            var L = DoLayout(g, ClientSize.Width, ClientSize.Height, -AutoScrollPosition.Y);
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            Render(g, L);
        }

        public Bitmap RenderToBitmap()
        {
            MapLayout L;
            float dpi;
            using (var g0 = CreateGraphics()) { L = DoLayout(g0, 0, 0, 0); dpi = g0.DpiX; }
            var bmp = new Bitmap((int)Math.Ceiling(L.W), (int)Math.Ceiling(L.H));
            bmp.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Theme.Bg);
                Render(g, DoLayout(g, 0, 0, 0));
            }
            return bmp;
        }

        void Render(Graphics g, MapLayout L)
        {
            float s = Theme.Scale;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // dot grid background
            using (var br = new SolidBrush(Color.FromArgb(22, 32, 48)))
                for (float x = 0; x < L.W; x += 22 * s)
                    for (float y = 0; y < L.H; y += 22 * s)
                        g.FillRectangle(br, x, y, 1.5f * s, 1.5f * s);

            using (var mag = new SolidBrush(Theme.Magenta))
            using (var cy = new SolidBrush(Theme.Cyan))
            using (var far = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.DrawString("◀ INBOUND  ·  clients connecting in  (" + data.Inbound.Count + ")", Theme.UIBold, mag, L.Margin, L.Margin);
                g.DrawString("OUTBOUND  ·  hosts this connects to  (" + data.Outbound.Count + ")  ▶", Theme.UIBold, cy,
                    new RectangleF(L.W / 2, L.Margin, L.W / 2 - L.Margin, L.Header), far);
            }

            // connectors
            for (int i = 0; i < L.In.Count; i++)
            {
                var r = L.In[i];
                var a = new PointF(r.Right, r.Top + r.Height / 2);
                var b = new PointF(L.Center.Left, Spread(L.Center, i, L.In.Count, s));
                Connector(g, a, b, data.Inbound[i], true);
            }
            for (int i = 0; i < L.Out.Count; i++)
            {
                var r = L.Out[i];
                var a = new PointF(L.Center.Right, Spread(L.Center, i, L.Out.Count, s));
                var b = new PointF(r.Left, r.Top + r.Height / 2);
                Connector(g, a, b, data.Outbound[i], false);
            }

            for (int i = 0; i < L.In.Count; i++) Node(g, L.In[i], data.Inbound[i]);
            for (int i = 0; i < L.Out.Count; i++) Node(g, L.Out[i], data.Outbound[i]);
            CenterNode(g, L.Center);

            // legend under the center node
            float lx = L.Center.Left, ly = L.Center.Bottom + 16 * s;
            foreach (var kv in new[] { "Internet", "LAN", "Loopback", "Other" })
            {
                using (var br = new SolidBrush(Theme.ScopeColor(kv))) g.FillEllipse(br, lx, ly + 4 * s, 8 * s, 8 * s);
                using (var br = new SolidBrush(Theme.Dim)) g.DrawString(kv, Theme.Small, br, lx + 11 * s, ly);
                lx += 11 * s + g.MeasureString(kv, Theme.Small).Width + 10 * s;
            }
            using (var br = new SolidBrush(Theme.Dim))
                g.DrawString("dashed = not currently connected", Theme.Small, br, L.Center.Left, ly + 18 * s);

            if (data.Inbound.Count == 0 && data.Outbound.Count == 0)
                using (var br = new SolidBrush(Theme.Dim))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString("No TCP connections recorded for this selection yet.", Theme.UI, br,
                        new RectangleF(0, L.Center.Bottom + 56 * s, L.W, 30 * s), sf);
        }

        static float Spread(RectangleF c, int i, int n, float s)
        {
            float pad = 10 * s;
            return c.Top + pad + (c.Height - 2 * pad) * (i + 0.5f) / Math.Max(1, n);
        }

        void Connector(Graphics g, PointF a, PointF b, MapNode n, bool inbound)
        {
            float s = Theme.Scale;
            Color c = Theme.ScopeColor(n.Scope);
            float dx = (b.X - a.X) / 2;
            var c1 = new PointF(a.X + dx, a.Y);
            var c2 = new PointF(b.X - dx, b.Y);
            if (n.Active && Theme.Effects)
                using (var glow = new Pen(Theme.A(c, 40), 6 * s)) g.DrawBezier(glow, a, c1, c2, b);
            using (var pen = new Pen(n.Active ? Theme.A(c, 220) : Theme.A(c, 90), (n.Active ? 1.6f : 1f) * s))
            {
                if (!n.Active) pen.DashStyle = DashStyle.Dash;
                pen.CustomEndCap = new AdjustableArrowCap(3.5f, 4.5f);
                g.DrawBezier(pen, a, c1, c2, b);
            }
            // "data flowing" pulses travelling along live links, in the direction of the connection
            if (n.Active && Theme.Effects && Animator.Active)
            {
                using (var flow = new Pen(Color.FromArgb(200, Color.White), 2.2f * s))
                {
                    flow.DashPattern = new[] { 1.5f, 7f };
                    flow.DashCap = DashCap.Round;
                    flow.DashOffset = -(float)((Animator.Now * 9) % 8.5);
                    g.DrawBezier(flow, a, c1, c2, b);
                }
            }
        }

        void Node(Graphics g, RectangleF r, MapNode n)
        {
            float s = Theme.Scale;
            Color c = Theme.ScopeColor(n.Scope);
            using (var path = Theme.Round(r, 7 * s))
            {
                if (n.Active && Theme.Effects)
                    using (var glow = new Pen(Theme.A(c, 45), 6 * s)) g.DrawPath(glow, path);
                using (var br = new SolidBrush(Theme.Panel2)) g.FillPath(br, path);
                using (var pen = new Pen(n.Active ? c : Theme.A(c, 90), (n.Active ? 1.5f : 1f) * s))
                {
                    if (!n.Active) pen.DashStyle = DashStyle.Dash;
                    g.DrawPath(pen, path);
                }
            }
            using (var br = new SolidBrush(n.Active ? c : Theme.A(c, 90)))
                g.FillRectangle(br, r.X + 1, r.Y + 8 * s, 3 * s, r.Height - 16 * s);

            float x = r.X + 12 * s, w = r.Width - 20 * s, y = r.Y + 6 * s;
            Line(g, n.Title, Theme.UIBold, n.Active ? Theme.Text : Theme.Dim, x, ref y, w);
            Line(g, n.Sub, Theme.Small, Theme.Dim, x, ref y, w);
            Line(g, n.Detail, Theme.Mono, n.Active ? c : Theme.A(c, 130), x, ref y, w);
        }

        static void Line(Graphics g, string text, Font f, Color c, float x, ref float y, float w)
        {
            float h = f.GetHeight(g);
            using (var br = new SolidBrush(c))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text ?? "", f, br, new RectangleF(x, y, w, h + 2), sf);
            y += h + 1;
        }

        void CenterNode(Graphics g, RectangleF r)
        {
            float s = Theme.Scale, pad = 12 * s;
            using (var path = Theme.Round(r, 10 * s))
            {
                if (Theme.Effects)
                {
                    using (var glow = new Pen(Theme.A(Theme.Magenta, 35), 14 * s)) g.DrawPath(glow, path);
                    using (var glow = new Pen(Theme.A(Theme.Magenta, 60), 6 * s)) g.DrawPath(glow, path);
                }
                using (var br = new LinearGradientBrush(r, Color.FromArgb(40, 18, 52), Color.FromArgb(14, 22, 40), 45f)) g.FillPath(br, path);
                using (var pen = new Pen(Theme.Magenta, 2 * s)) g.DrawPath(pen, path);
            }
            float y = r.Y + pad, tw = r.Width - 2 * pad;
            using (var br = new SolidBrush(Color.White))
            {
                var sz = g.MeasureString(data.Center, Theme.Title, (int)tw);
                g.DrawString(data.Center, Theme.Title, br, new RectangleF(r.X + pad, y, tw, sz.Height));
                y += sz.Height + 4 * s;
            }
            if (data.CenterSub.Length > 0)
                using (var br = new SolidBrush(Theme.Dim))
                {
                    var sz = g.MeasureString(data.CenterSub, Theme.Small, (int)tw);
                    g.DrawString(data.CenterSub, Theme.Small, br, new RectangleF(r.X + pad, y, tw, sz.Height));
                    y += sz.Height + 8 * s;
                }
            if (data.CenterDetail.Length > 0)
                using (var br = new SolidBrush(Theme.Lime))
                {
                    var sz = g.MeasureString(data.CenterDetail, Theme.Mono, (int)tw);
                    g.DrawString(data.CenterDetail, Theme.Mono, br, new RectangleF(r.X + pad, y, tw, sz.Height));
                }
        }
    }
}
