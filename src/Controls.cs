using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PortsInUse
{
    // =====================================================================
    //  Theme
    // =====================================================================
    public static class Theme
    {
        public static readonly Color Bg = Hex("#0A0E14");
        public static readonly Color Panel = Hex("#0F1520");
        public static readonly Color Panel2 = Hex("#151D2B");
        public static readonly Color PanelHi = Hex("#1D2940");
        public static readonly Color Border = Hex("#22304A");
        public static readonly Color Text = Hex("#D5E1F0");
        public static readonly Color Dim = Hex("#71839E");
        public static readonly Color Cyan = Hex("#00E5FF");
        public static readonly Color Magenta = Hex("#FF2BD6");
        public static readonly Color Lime = Hex("#3DFF8A");
        public static readonly Color Amber = Hex("#FFB547");
        public static readonly Color Red = Hex("#FF4D6D");
        public static readonly Color Violet = Hex("#A07CFF");

        public static bool Effects = true;
        public static float Scale = 1f;
        public static Font UI, UIBold, Mono, Title, Big, Small, Icons, Brand;
        public static string MonoName = "Consolas";

        // Undocumented uxtheme opt-in so "DarkMode_Explorer" gives dark scrollbars (Win10 1903+).
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);

        public static void Init(float scale)
        {
            Scale = scale;
            try { if (Environment.OSVersion.Version.Build >= 18362) SetPreferredAppMode(2); } catch { }
            using (var f = new Font("Cascadia Mono", 9f)) if (f.Name == "Cascadia Mono") MonoName = "Cascadia Mono";
            UI = new Font("Segoe UI", 9f);
            UIBold = new Font("Segoe UI Semibold", 9f);
            Small = new Font("Segoe UI", 7.75f);
            Mono = new Font(MonoName, 8.75f);
            Title = new Font("Segoe UI Semibold", 15f);
            Big = new Font("Segoe UI Light", 22f);
            Icons = new Font("Segoe MDL2 Assets", 11f);
            Brand = new Font(MonoName, 12f, FontStyle.Bold);
        }

        static Color Hex(string h) { return ColorTranslator.FromHtml(h); }
        public static Color A(Color c, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c); }
        public static int S(float px) { return (int)Math.Round(px * Scale); }

        public static Color ScopeColor(string scope)
        {
            switch (scope)
            {
                case "Internet": return Amber;
                case "LAN": return Cyan;
                case "Loopback": return Dim;
                default: return Violet;
            }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Text with a neon glow (glow only when effects are on).</summary>
        public static void GlowText(Graphics g, string text, Font f, Color c, PointF at)
        {
            if (Effects)
            {
                using (var path = new GraphicsPath())
                {
                    float em = f.SizeInPoints * g.DpiY / 72f;
                    path.AddString(text, f.FontFamily, (int)f.Style, em, at, StringFormat.GenericTypographic);
                    var old = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    for (int w = 7; w >= 2; w -= 2)
                        using (var pen = new Pen(A(c, 28), w * Scale) { LineJoin = LineJoin.Round })
                            g.DrawPath(pen, path);
                    using (var br = new SolidBrush(c)) g.FillPath(br, path);
                    g.SmoothingMode = old;
                    return;
                }
            }
            using (var br = new SolidBrush(c)) g.DrawString(text, f, br, at, StringFormat.GenericTypographic);
        }

        /// <summary>A line with neon glow.</summary>
        public static void GlowLines(Graphics g, Color c, float width, PointF[] pts)
        {
            if (pts.Length < 2) return;
            if (Effects)
            {
                using (var p1 = new Pen(A(c, 35), width * 5) { LineJoin = LineJoin.Round })
                    g.DrawLines(p1, pts);
                using (var p2 = new Pen(A(c, 80), width * 2.5f) { LineJoin = LineJoin.Round })
                    g.DrawLines(p2, pts);
            }
            using (var p3 = new Pen(c, width) { LineJoin = LineJoin.Round }) g.DrawLines(p3, pts);
        }

        public static void Scanlines(Graphics g, Rectangle r)
        {
            if (!Effects) return;
            using (var pen = new Pen(Color.FromArgb(28, 0, 0, 0)))
                for (int y = r.Top; y < r.Bottom; y += 3) g.DrawLine(pen, r.Left, y, r.Right, y);
        }

        public static Button MakeButton(string text, Color accent)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Panel2,
                ForeColor = accent,
                Font = UIBold,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(8), S(2), S(8), S(2)),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = A(accent, 160);
            b.FlatAppearance.MouseOverBackColor = PanelHi;
            b.FlatAppearance.MouseDownBackColor = A(accent, 60);
            return b;
        }

        // ---- native helpers for dark chrome ----
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public static void DarkTitleBar(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                int border = ColorTranslator.ToWin32(Border);
                DwmSetWindowAttribute(f.Handle, 34, ref border, 4);   // border colour (Win11)
                int caption = ColorTranslator.ToWin32(Bg);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);  // caption colour (Win11)
            }
            catch { }
        }

        public static void DarkScrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
            foreach (Control child in c.Controls)
                if (child is ScrollBar) { try { SetWindowTheme(child.Handle, "DarkMode_Explorer", null); } catch { } }
        }

        public static void Cue(TextBox tb, string text)
        {
            try { SendMessage(tb.Handle, 0x1501, (IntPtr)1, text); } catch { }
        }
    }

    // ---- dark context menu ----
    class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.PanelHi; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.PanelHi; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.PanelHi; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Panel2; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Panel2; } }
        public override Color MenuItemBorder { get { return Theme.A(Theme.Cyan, 120); } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Panel; } }
        public override Color CheckBackground { get { return Theme.Panel2; } }
        public override Color CheckSelectedBackground { get { return Theme.PanelHi; } }
        public override Color CheckPressedBackground { get { return Theme.PanelHi; } }
    }

    public class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColorTable()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Dim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using (var br = new SolidBrush(Theme.Cyan))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString("✓", Theme.UIBold, br, r, sf);
        }
    }

    // =====================================================================
    //  Row + virtual grid
    // =====================================================================
    public class Row
    {
        public string Key;
        public object[] V;
        public bool Active = true;
        public string App;     // application this row belongs to (for "Show on Map")
    }

    /// <summary>Dark, virtual-mode DataGridView with click-to-sort columns and stable selection across refreshes.</summary>
    public class VirtualList
    {
        public readonly DataGridView Grid;
        readonly string[] headers;
        List<Row> rows = new List<Row>();
        int sortCol;
        bool asc;
        public Func<Row, int, Color> CellColor;   // optional per-cell accent

        public VirtualList(string[] headers, int[] widths, int sortColumn, bool ascending)
        {
            this.headers = headers;
            sortCol = sortColumn;
            asc = ascending;

            Grid = new DataGridView
            {
                VirtualMode = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Theme.Panel,
                GridColor = Theme.Border,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = Theme.S(30),
                ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable,
                StandardTab = true
            };
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Grid, true, null);
            Grid.RowTemplate.Height = Theme.S(24);
            Grid.DefaultCellStyle.BackColor = Theme.Panel;
            Grid.DefaultCellStyle.ForeColor = Theme.Text;
            Grid.DefaultCellStyle.SelectionBackColor = Theme.A(Theme.Cyan, 55);
            Grid.DefaultCellStyle.SelectionForeColor = Color.White;
            Grid.DefaultCellStyle.Font = Theme.Mono;
            Grid.DefaultCellStyle.Padding = new Padding(Theme.S(4), 0, Theme.S(4), 0);
            Grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(18, 25, 37);
            Grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Bg;
            Grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Cyan;
            Grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Bg;
            Grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Cyan;
            Grid.ColumnHeadersDefaultCellStyle.Font = Theme.UIBold;
            Grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Theme.S(4), 0, 0, 0);
            Grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;

            for (int i = 0; i < headers.Length; i++)
            {
                Grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = headers[i].ToUpperInvariant(),
                    Width = Theme.S(widths[i]),
                    SortMode = DataGridViewColumnSortMode.Programmatic
                });
            }
            UpdateGlyph();

            Grid.CellValueNeeded += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < rows.Count && e.ColumnIndex < rows[e.RowIndex].V.Length)
                    e.Value = Text(rows[e.RowIndex].V[e.ColumnIndex]);
            };
            Grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
                var r = rows[e.RowIndex];
                Color c = CellColor != null ? CellColor(r, e.ColumnIndex) : Color.Empty;
                if (!r.Active) e.CellStyle.ForeColor = c.IsEmpty ? Theme.Dim : Theme.A(c, 110);
                else if (!c.IsEmpty) e.CellStyle.ForeColor = c;
            };
            Grid.ColumnHeaderMouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (e.ColumnIndex == sortCol) asc = !asc;
                else { sortCol = e.ColumnIndex; asc = true; }
                UpdateGlyph();
                SetRows(rows);
            };
            Grid.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C) { CopySelected(); e.Handled = true; }
            };
            Grid.HandleCreated += (s, e) => Theme.DarkScrollbars(Grid);
        }

        public static string Text(object v)
        {
            if (v == null) return "";
            if (v is DateTime) return Fmt.Time((DateTime)v);
            if (v is bool) return (bool)v ? "●" : "";
            return v.ToString();
        }

        void UpdateGlyph()
        {
            for (int i = 0; i < Grid.Columns.Count; i++)
                Grid.Columns[i].HeaderCell.SortGlyphDirection = i == sortCol ? (asc ? SortOrder.Ascending : SortOrder.Descending) : SortOrder.None;
        }

        static int CompareValues(object a, object b)
        {
            bool ea = a == null || (a is string && ((string)a).Length == 0);
            bool eb = b == null || (b is string && ((string)b).Length == 0);
            if (ea && eb) return 0;
            if (ea) return 1;
            if (eb) return -1;
            if (a.GetType() == b.GetType() && a is IComparable)
            {
                if (a is string) return string.Compare((string)a, (string)b, StringComparison.CurrentCultureIgnoreCase);
                return ((IComparable)a).CompareTo(b);
            }
            return string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCultureIgnoreCase);
        }

        public void SetRows(List<Row> newRows)
        {
            var selected = new HashSet<string>(SelectedRows().Select(r => r.Key));
            int c = sortCol;
            bool a = asc;
            var sorted = newRows.ToList();
            sorted.Sort((x, y) =>
            {
                int r = CompareValues(x.V[c], y.V[c]);
                if (r == 0) r = string.CompareOrdinal(x.Key, y.Key);
                return a ? r : -r;
            });

            int first = Grid.RowCount > 0 ? Grid.FirstDisplayedScrollingRowIndex : -1;
            rows = sorted;
            if (Grid.RowCount != rows.Count) Grid.RowCount = rows.Count;
            Grid.ClearSelection();
            if (selected.Count > 0)
                for (int i = 0; i < rows.Count; i++)
                    if (selected.Contains(rows[i].Key)) Grid.Rows[i].Selected = true;
            if (first >= 0 && first < rows.Count)
            {
                try { if (Grid.FirstDisplayedScrollingRowIndex != first) Grid.FirstDisplayedScrollingRowIndex = first; } catch { }
            }
            Grid.Invalidate();
        }

        public int Count { get { return rows.Count; } }

        public List<Row> SelectedRows()
        {
            var list = new List<Row>();
            foreach (DataGridViewRow r in Grid.SelectedRows)
                if (r.Index >= 0 && r.Index < rows.Count) list.Add(rows[r.Index]);
            return list.OrderBy(r => rows.IndexOf(r)).ToList();
        }

        public void CopySelected()
        {
            var sel = SelectedRows();
            if (sel.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", headers));
            foreach (var r in sel) sb.AppendLine(string.Join("\t", r.V.Select(v => v is bool ? ((bool)v ? "Yes" : "No") : Text(v))));
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        public void ExportCsv(string path)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                w.WriteLine(string.Join(",", headers.Select(Csv)));
                foreach (var r in rows)
                    w.WriteLine(string.Join(",", r.V.Select(v =>
                        Csv(v is DateTime ? ((DateTime)v).ToString("yyyy-MM-dd HH:mm:ss")
                          : v is bool ? ((bool)v ? "Yes" : "No") : Text(v)))));
            }
        }

        static string Csv(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    // =====================================================================
    //  Neon graph
    // =====================================================================
    public class GlowGraph : Control
    {
        readonly List<float[]> points = new List<float[]>();
        readonly List<double> times = new List<double>();   // Animator.Now when each point arrived
        public string[] SeriesNames = new string[0];
        public Color[] SeriesColors = { Theme.Cyan, Theme.Magenta, Theme.Lime, Theme.Amber };
        public int MaxPoints = 900;
        public string Caption = "";
        public double IntervalSec = 2;      // expected time between samples
        bool frozen;
        double frozenAt;
        float shownMax = 10;                // eased y-axis scale

        public GlowGraph()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        public void AddPoint(float[] values)
        {
            points.Add(values);
            times.Add(Animator.Now);
            if (points.Count > MaxPoints) { points.RemoveAt(0); times.RemoveAt(0); }
            if (!Animator.Active) Invalidate();
        }

        /// <summary>Stop the graph scrolling while monitoring is paused.</summary>
        public void Freeze(bool on)
        {
            if (on && !frozen) frozenAt = Animator.Now;
            frozen = on;
            Invalidate();
        }

        public void Clear() { points.Clear(); times.Clear(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float s = Theme.Scale;
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(outer, 8 * s))
            {
                using (var br = new SolidBrush(Theme.Panel)) g.FillPath(br, path);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }

            float labelW = 40 * s, top = 34 * s;
            var plot = new RectangleF(labelW, top, Math.Max(10, Width - labelW - 14 * s), Math.Max(10, Height - top - 14 * s));

            // header: caption + legend
            using (var dim = new SolidBrush(Theme.Dim))
                g.DrawString(Caption.ToUpperInvariant(), Theme.UIBold, dim, 12 * s, 9 * s);
            float lx = Width - 12 * s;
            for (int i = SeriesNames.Length - 1; i >= 0; i--)
            {
                Color c = SeriesColors[i % SeriesColors.Length];
                string t = SeriesNames[i];
                if (points.Count > 0 && i < points[points.Count - 1].Length) t += "  " + points[points.Count - 1][i].ToString("0");
                float w = g.MeasureString(t, Theme.UI).Width;
                lx -= w;
                using (var br = new SolidBrush(c)) g.DrawString(t, Theme.UI, br, lx, 9 * s);
                lx -= 12 * s;
                using (var br = new SolidBrush(c)) g.FillEllipse(br, lx, 14 * s, 7 * s, 7 * s);
                lx -= 16 * s;
            }

            float max = 1;
            foreach (var p in points) foreach (var v in p) if (v > max) max = v;
            float target = NiceCeil(max * 1.15f);
            // ease the axis scale when animating so rescales glide instead of snapping
            if (Animator.Active && Math.Abs(shownMax - target) > 0.01f) shownMax += (target - shownMax) * 0.12f;
            else shownMax = target;
            float nice = shownMax;

            // Time-based x positions. The newest sample enters from just off the right edge and
            // slides in over one interval, so motion is continuous between samples (TMOG-style).
            // Without animation, renderNow is pinned so the newest sample sits on the right edge.
            float step = 3 * s, cell = 24 * s;
            double interval = Math.Max(0.25, IntervalSec);
            double pps = step / interval;
            double lastT = times.Count > 0 ? times[times.Count - 1] : 0;
            double renderNow = Animator.Active ? (frozen ? frozenAt : Animator.Now) : lastT + interval;
            if (Animator.Active && !frozen) renderNow = Math.Min(renderNow, lastT + interval * 1.5); // don't drift far ahead if a sample is late

            g.SmoothingMode = SmoothingMode.None;
            using (var gp = new Pen(Color.FromArgb(24, 36, 54)))
            {
                for (int i = 0; i <= 4; i++)
                {
                    float y = plot.Bottom - plot.Height * i / 4f;
                    g.DrawLine(gp, plot.Left, y, plot.Right, y);
                }
                float off = (float)((renderNow * pps) % cell);
                for (float x = plot.Right - off; x >= plot.Left; x -= cell) g.DrawLine(gp, x, plot.Top, x, plot.Bottom);
            }
            using (var dim = new SolidBrush(Theme.Dim))
            using (var far = new StringFormat { Alignment = StringAlignment.Far })
            {
                for (int i = 0; i <= 4; i += 2)
                {
                    float y = plot.Bottom - plot.Height * i / 4f;
                    g.DrawString((nice * i / 4f).ToString("0"), Theme.Small, dim, new RectangleF(0, y - 7 * s, labelW - 6 * s, 16 * s), far);
                }
            }

            if (points.Count >= 2)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.SetClip(plot);
                int series = points.Max(p => p.Length);
                for (int si = series - 1; si >= 0; si--)
                {
                    var pts = new List<PointF>();
                    for (int i = points.Count - 1; i >= 0; i--)
                    {
                        float x = plot.Right - (float)((renderNow - interval - times[i]) * pps);
                        float v = si < points[i].Length ? points[i][si] : 0;
                        pts.Add(new PointF(x, plot.Bottom - v / nice * plot.Height));
                        if (x < plot.Left - step) break;
                    }
                    if (pts.Count < 2) continue;
                    Color c = SeriesColors[si % SeriesColors.Length];
                    // gradient area fill
                    var area = new List<PointF>(pts);
                    area.Add(new PointF(pts[pts.Count - 1].X, plot.Bottom));
                    area.Add(new PointF(pts[0].X, plot.Bottom));
                    using (var lg = new LinearGradientBrush(new PointF(0, plot.Top), new PointF(0, plot.Bottom + 1), Theme.A(c, si == 0 ? 70 : 35), Theme.A(c, 0)))
                        g.FillPolygon(lg, area.ToArray());
                    Theme.GlowLines(g, c, 1.6f * s, pts.ToArray());
                    if (si == 0 && Theme.Effects)
                    {
                        // glowing head sits where the line crosses the right edge
                        var head = pts[0];
                        if (pts[0].X > plot.Right && pts[1].X <= plot.Right)
                        {
                            float t = (plot.Right - pts[1].X) / Math.Max(0.001f, pts[0].X - pts[1].X);
                            head = new PointF(plot.Right, pts[1].Y + (pts[0].Y - pts[1].Y) * t);
                        }
                        else if (pts[0].X > plot.Right) head = new PointF(plot.Right, pts[0].Y);
                        using (var br = new SolidBrush(Theme.A(c, 60))) g.FillEllipse(br, head.X - 6 * s, head.Y - 6 * s, 12 * s, 12 * s);
                        using (var br = new SolidBrush(c)) g.FillEllipse(br, head.X - 2.5f * s, head.Y - 2.5f * s, 5 * s, 5 * s);
                    }
                }
                g.ResetClip();
            }
            else
            {
                using (var dim = new SolidBrush(Theme.Dim))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("collecting samples…", Theme.UI, dim, plot, sf);
            }
            Theme.Scanlines(g, Rectangle.Round(plot));
        }

        static float NiceCeil(float v)
        {
            if (v <= 10) return 10;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (double m in new[] { 1, 2, 2.5, 5, 10 })
                if (m * mag >= v) return (float)(m * mag);
            return (float)(10 * mag);
        }
    }

    // =====================================================================
    //  Stat tile with sparkline
    // =====================================================================
    public class StatTile : Control
    {
        public string Caption = "", Value = "—", Sub = "";
        public Color Accent = Theme.Cyan;
        readonly List<float> spark = new List<float>();

        public StatTile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        double target, shown;
        bool numeric;

        public void Set(string value, string sub, float sparkValue)
        {
            Value = value; Sub = sub;
            double v;
            numeric = double.TryParse(value, out v);
            if (numeric) { target = v; if (!Animator.Active) shown = v; }
            spark.Add(sparkValue);
            if (spark.Count > 60) spark.RemoveAt(0);
            Invalidate();
        }

        /// <summary>Advance the count-up animation. Returns true if the tile needs repainting.</summary>
        public bool Step(double dt)
        {
            if (!numeric || shown == target) return false;
            if (Math.Abs(target - shown) < 0.5) shown = target;
            else shown += (target - shown) * (1 - Math.Exp(-dt * 9));
            return true;
        }

        string DisplayValue { get { return numeric ? Math.Round(shown).ToString("0") : Value; } }

        public void ClearSpark() { spark.Clear(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Theme.Scale;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 8 * s))
            {
                using (var br = new LinearGradientBrush(r, Theme.Panel2, Theme.Panel, 90f)) g.FillPath(br, path);
                using (var pen = new Pen(Theme.A(Accent, 90))) g.DrawPath(pen, path);
            }
            // accent bar
            using (var br = new SolidBrush(Accent)) g.FillRectangle(br, 0, 10 * s, 3 * s, Height - 20 * s);
            if (Theme.Effects)
                using (var br = new SolidBrush(Theme.A(Accent, 40))) g.FillRectangle(br, 0, 8 * s, 7 * s, Height - 16 * s);

            using (var dim = new SolidBrush(Theme.Dim))
                g.DrawString(Caption.ToUpperInvariant(), Theme.Small, dim, 14 * s, 9 * s);
            Theme.GlowText(g, DisplayValue, Theme.Big, Accent, new PointF(12 * s, 24 * s));
            using (var dim = new SolidBrush(Theme.Dim))
                g.DrawString(Sub, Theme.Small, dim, new RectangleF(14 * s, Height - 22 * s, Width - 20 * s, 16 * s),
                    new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

            if (spark.Count >= 2)
            {
                float max = Math.Max(1, spark.Max());
                float sw = Width * 0.42f, sh = Height * 0.34f;
                float sx = Width - sw - 10 * s, sy = 26 * s;
                var pts = new PointF[spark.Count];
                for (int i = 0; i < spark.Count; i++)
                    pts[i] = new PointF(sx + sw * i / 59f + (60 - spark.Count) * sw / 59f, sy + sh - spark[i] / max * sh);
                Theme.GlowLines(g, Theme.A(Accent, 220), 1.3f * s, pts);
            }
        }
    }

    // =====================================================================
    //  Sidebar navigation button
    // =====================================================================
    public class NavButton : Control
    {
        public string Glyph = "";
        public string Badge = "";
        bool selected, hover;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public NavButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Height = Theme.S(38);
            Dock = DockStyle.Top;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        public void SetBadge(string b) { if (b != Badge) { Badge = b; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Theme.Scale;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(8 * s, 3 * s, Width - 16 * s, Height - 6 * s);
            if (selected || hover)
                using (var path = Theme.Round(r, 6 * s))
                using (var br = new SolidBrush(selected ? Theme.PanelHi : Theme.Panel2))
                    g.FillPath(br, path);
            if (selected)
            {
                if (Theme.Effects) using (var br = new SolidBrush(Theme.A(Theme.Cyan, 50))) g.FillRectangle(br, 0, r.Y + 2 * s, 8 * s, r.Height - 4 * s);
                using (var br = new SolidBrush(Theme.Cyan)) g.FillRectangle(br, 0, r.Y + 4 * s, 3 * s, r.Height - 8 * s);
            }
            Color fc = selected ? Theme.Cyan : (hover ? Theme.Text : Theme.Dim);
            using (var br = new SolidBrush(fc))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
            {
                g.DrawString(Glyph, Theme.Icons, br, new RectangleF(18 * s, r.Y, 26 * s, r.Height), sf);
                using (var tb = new SolidBrush(selected ? Theme.Text : fc))
                    g.DrawString(Text, selected ? Theme.UIBold : Theme.UI, tb, new RectangleF(46 * s, r.Y, r.Width - 90 * s, r.Height), sf);
            }
            if (Badge.Length > 0)
            {
                float bw = g.MeasureString(Badge, Theme.Small).Width + 10 * s;
                var br2 = new RectangleF(r.Right - bw - 6 * s, r.Y + r.Height / 2 - 9 * s, bw, 18 * s);
                using (var path = Theme.Round(br2, 9 * s))
                using (var fill = new SolidBrush(Theme.A(selected ? Theme.Cyan : Theme.Dim, 40)))
                    g.FillPath(fill, path);
                using (var tb = new SolidBrush(selected ? Theme.Cyan : Theme.Dim))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(Badge, Theme.Small, tb, br2, sf);
            }
        }
    }

    // =====================================================================
    //  Toggle chip (pill checkbox)
    // =====================================================================
    public class ToggleChip : Control
    {
        bool isChecked, hover;
        public event EventHandler CheckedChanged;
        public Color Accent = Theme.Cyan;

        public bool Checked
        {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        }

        public ToggleChip(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            using (var g = CreateGraphics())
                Size = new Size((int)(g.MeasureString(text, Theme.UI).Width + Theme.S(34)), Theme.S(26));
            Margin = new Padding(Theme.S(3), Theme.S(4), Theme.S(3), Theme.S(4));
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Theme.Scale;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, r.Height / 2))
            {
                using (var br = new SolidBrush(isChecked ? Theme.A(Accent, 38) : (hover ? Theme.PanelHi : Theme.Panel2))) g.FillPath(br, path);
                using (var pen = new Pen(isChecked ? Accent : Theme.Border)) g.DrawPath(pen, path);
            }
            float d = 8 * s;
            var dot = new RectangleF(10 * s, (Height - d) / 2, d, d);
            if (isChecked && Theme.Effects)
                using (var br = new SolidBrush(Theme.A(Accent, 70))) g.FillEllipse(br, dot.X - 3 * s, dot.Y - 3 * s, d + 6 * s, d + 6 * s);
            using (var br = new SolidBrush(isChecked ? Accent : Theme.Dim)) g.FillEllipse(br, dot);
            using (var br = new SolidBrush(isChecked ? Theme.Text : Theme.Dim))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
                g.DrawString(Text, Theme.UI, br, new RectangleF(22 * s, 0, Width - 24 * s, Height), sf);
        }
    }
}
