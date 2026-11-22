using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PortsInUse
{
    public class MainForm : Form
    {
        const string WholePc = "⌂  Whole computer";

        readonly TrafficModel model = new TrafficModel();
        readonly ProcessResolver resolver = new ProcessResolver();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer autosaveTimer = new System.Windows.Forms.Timer();
        readonly bool elevated = Native.IsElevated();
        bool busy, running;
        int intervalSec = 2;
        int frameCount;
        string currentView = "Summary";
        string sessionPath;

        // chrome
        Panel header, toolbar, content;
        Label statusBar;
        Control brand, sideStats;
        TextBox txtFilter;
        ToggleChip chipLoop, chipActive, chipEph;
        Button btnPause, btnSpeed, btnMenu, btnMap;
        Label lblShowing;
        ContextMenuStrip menu, speedMenu;
        ToolStripMenuItem miDns, miAutosave, miEffects, miSmooth, miTopmost;
        readonly Dictionary<string, NavButton> nav = new Dictionary<string, NavButton>();
        readonly Dictionary<string, Control> views = new Dictionary<string, Control>();
        readonly Dictionary<string, string> descriptions = new Dictionary<string, string>();

        // views
        StatTile tConns, tListen, tHosts, tApps, tDeps, tSamples;
        GlowGraph gConns, gHosts;
        VirtualList topApps, topHosts, appsList, liveList, depList, lisList, hostList;
        ListBox mapList;
        MapPanel map;
        readonly Dictionary<string, bool> mapAppActive = new Dictionary<string, bool>();
        bool updatingMapList;

        public MainForm()
        {
            float scale;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Theme.Init(scale);

            Text = "Ports In Use — Network Dependency Monitor" + (elevated ? "  (Administrator)" : "");
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;
            Size = new Size(Theme.S(1440), Theme.S(860));
            MinimumSize = new Size(Theme.S(1000), Theme.S(600));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();

            timer.Tick += Timer_Tick;
            timer.Interval = intervalSec * 1000;
            autosaveTimer.Interval = 5 * 60 * 1000;
            autosaveTimer.Tick += (s, e) => AutoSave();
            Animator.Frame += OnFrame;
            Resize += (s, e) => UpdateAnimatorState();

            Load += (s, e) => Theme.DarkTitleBar(this);
            Shown += (s, e) =>
            {
                ShowView("Summary");
                StartMonitoring();
                Animator.Start(this);
                UpdateAnimatorState();
            };
            FormClosing += (s, e) =>
            {
                timer.Stop();
                if (miAutosave.Checked) AutoSave();
            };
            KeyDown += MainForm_KeyDown;
        }

        // =====================================================================
        //  UI construction
        // =====================================================================
        void BuildUi()
        {
            // ---------------- sidebar ----------------
            var sidebar = new Panel { Dock = DockStyle.Left, Width = Theme.S(214), BackColor = Theme.Bg };
            sidebar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            };

            var navDefs = new[]
            {
                new[] { "Summary", "", "Network activity overview for " + Environment.MachineName },
                new[] { "Applications", "", "Every app seen using the network: who it serves and what it talks to" },
                new[] { "Connections", "", "Live socket table (the netstat -ano view), refreshed every sample" },
                new[] { "Dependencies", "", "Every app ⇄ remote endpoint relationship observed over time" },
                new[] { "Listening", "", "Ports open for incoming traffic, and the process that owns each" },
                new[] { "Remote Hosts", "", "Every remote device seen, and which local apps talk to it" },
                new[] { "Map", "", "Dependency map: who connects in to an app, and what it connects out to" }
            };

            sideStats = new DoubleBufferedPanel { Dock = DockStyle.Bottom, Height = Theme.S(120), BackColor = Theme.Bg };
            sideStats.Paint += SideStats_Paint;
            sidebar.Controls.Add(sideStats);

            for (int i = navDefs.Length - 1; i >= 0; i--)
            {
                string name = navDefs[i][0];
                var b = new NavButton { Text = name, Glyph = navDefs[i][1] };
                b.Click += (s, e) => ShowView(name);
                nav[name] = b;
                descriptions[name] = navDefs[i][2];
                sidebar.Controls.Add(b);
            }
            var spacer = new Panel { Dock = DockStyle.Top, Height = Theme.S(8), BackColor = Theme.Bg };
            sidebar.Controls.Add(spacer);
            brand = new DoubleBufferedPanel { Dock = DockStyle.Top, Height = Theme.S(74), BackColor = Theme.Bg };
            brand.Paint += Brand_Paint;
            sidebar.Controls.Add(brand);

            // ---------------- main column ----------------
            var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(Theme.S(16), Theme.S(4), Theme.S(16), Theme.S(10)) };

            // toolbar: filter + chips
            toolbar = new Panel { Dock = DockStyle.Top, Height = Theme.S(46), BackColor = Theme.Bg, Padding = new Padding(Theme.S(16), Theme.S(6), Theme.S(16), Theme.S(4)) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            var filterBox = new Panel { Width = Theme.S(300), Height = Theme.S(28), BackColor = Theme.Panel2, Padding = new Padding(Theme.S(30), Theme.S(6), Theme.S(8), Theme.S(2)), Margin = new Padding(0, Theme.S(3), Theme.S(10), 0) };
            filterBox.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(txtFilter.Focused ? Theme.Cyan : Theme.Border)) g.DrawRectangle(pen, 0, 0, filterBox.Width - 1, filterBox.Height - 1);
                using (var br = new SolidBrush(Theme.Dim)) g.DrawString("", Theme.Icons, br, Theme.S(7), Theme.S(5));
            };
            txtFilter = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Panel2, ForeColor = Theme.Text, Dock = DockStyle.Fill, Font = Theme.UI };
            txtFilter.HandleCreated += (s, e) => Theme.Cue(txtFilter, "Filter: app, IP, host, port…  (Ctrl+F)");
            txtFilter.TextChanged += (s, e) => RefreshCurrentView();
            txtFilter.GotFocus += (s, e) => filterBox.Invalidate();
            txtFilter.LostFocus += (s, e) => filterBox.Invalidate();
            filterBox.Controls.Add(txtFilter);
            flow.Controls.Add(filterBox);

            chipLoop = new ToggleChip("Hide loopback");
            chipActive = new ToggleChip("Active only") { Accent = Theme.Lime };
            chipEph = new ToggleChip("Hide ephemeral listeners") { Accent = Theme.Violet };
            foreach (var c in new[] { chipLoop, chipActive, chipEph })
            {
                c.CheckedChanged += (s, e) => RefreshCurrentView();
                flow.Controls.Add(c);
            }
            lblShowing = new Label { AutoSize = true, ForeColor = Theme.Dim, Font = Theme.Small, Margin = new Padding(Theme.S(10), Theme.S(9), 0, 0) };
            flow.Controls.Add(lblShowing);
            toolbar.Controls.Add(flow);

            // header: title + actions
            header = new DoubleBufferedPanel { Dock = DockStyle.Top, Height = Theme.S(70), BackColor = Theme.Bg, Padding = new Padding(Theme.S(16), Theme.S(16), Theme.S(16), 0) };
            header.Paint += Header_Paint;
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Bg
            };
            btnMenu = Theme.MakeButton("⋯", Theme.Text);
            btnMenu.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            btnPause = Theme.MakeButton("❚❚  Pause", Theme.Amber);
            btnSpeed = Theme.MakeButton("Speed: Normal (2 s)  ▾", Theme.Cyan);
            btnMap = Theme.MakeButton("◈  Show on map", Theme.Magenta);
            foreach (var b in new[] { btnMenu, btnPause, btnSpeed, btnMap })
            {
                b.Margin = new Padding(Theme.S(6), 0, 0, 0);
                b.Height = Theme.S(32);
                actions.Controls.Add(b);
            }
            btnPause.Click += (s, e) => { if (running) StopMonitoring(); else StartMonitoring(); };
            btnMap.Click += (s, e) => ShowSelectedOnMap();
            header.Controls.Add(actions);

            BuildMenus();
            btnMenu.Click += (s, e) => menu.Show(btnMenu, new Point(btnMenu.Width - menu.Width, btnMenu.Height));
            btnSpeed.Click += (s, e) => speedMenu.Show(btnSpeed, new Point(0, btnSpeed.Height));

            statusBar = new Label
            {
                Dock = DockStyle.Bottom, Height = Theme.S(26), BackColor = Theme.Panel, ForeColor = Theme.Dim,
                Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Theme.S(14), 0, 0, 0)
            };

            BuildViews();

            main.Controls.Add(content);
            main.Controls.Add(toolbar);
            main.Controls.Add(header);
            main.Controls.Add(statusBar);

            Controls.Add(main);
            Controls.Add(sidebar);
        }

        void BuildMenus()
        {
            menu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.UI, ShowImageMargin = true };
            menu.Items.Add("New session (clear history)", null, (s, e) => ClearHistory());
            menu.Items.Add("Load session…", null, (s, e) => LoadSession());
            menu.Items.Add("Save session…", null, (s, e) => SaveSession());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Export current list to CSV…", null, (s, e) => ExportCsv());
            menu.Items.Add("Save map as PNG…", null, (s, e) => SaveMapPng());
            menu.Items.Add(new ToolStripSeparator());
            miDns = new ToolStripMenuItem("Resolve host names (reverse DNS)") { CheckOnClick = true, Checked = true };
            miDns.CheckedChanged += (s, e) => DnsCache.Enabled = miDns.Checked;
            miAutosave = new ToolStripMenuItem("Auto-save session every 5 minutes") { CheckOnClick = true };
            miAutosave.CheckedChanged += (s, e) =>
            {
                if (miAutosave.Checked) { autosaveTimer.Start(); AutoSave(); } else autosaveTimer.Stop();
            };
            miEffects = new ToolStripMenuItem("Visual effects (glow, scanlines, pulse)") { CheckOnClick = true, Checked = true };
            miEffects.CheckedChanged += (s, e) =>
            {
                Theme.Effects = miEffects.Checked;
                UpdateAnimatorState();
                InvalidateAll(this);
            };
            miSmooth = new ToolStripMenuItem("Smooth animation (60 fps)") { CheckOnClick = true, Checked = true };
            miSmooth.CheckedChanged += (s, e) => { UpdateAnimatorState(); InvalidateAll(this); };
            miTopmost = new ToolStripMenuItem("Always on top") { CheckOnClick = true };
            miTopmost.CheckedChanged += (s, e) => TopMost = miTopmost.Checked;
            menu.Items.AddRange(new ToolStripItem[] { miDns, miAutosave, miEffects, miSmooth, miTopmost });
            menu.Items.Add(new ToolStripSeparator());
            if (!elevated) menu.Items.Add("Restart as Administrator", null, (s, e) => RestartElevated());
            menu.Items.Add("About", null, (s, e) => ShowAbout());
            menu.Items.Add("Exit", null, (s, e) => Close());

            speedMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.UI };
            var speeds = new[] { new object[] { "High (1 s)", 1 }, new object[] { "Normal (2 s)", 2 }, new object[] { "Low (5 s)", 5 }, new object[] { "Very low (30 s)", 30 } };
            foreach (var sp in speeds)
            {
                string label = (string)sp[0];
                int sec = (int)sp[1];
                var mi = new ToolStripMenuItem(label) { Checked = sec == intervalSec };
                mi.Click += (s, e) =>
                {
                    intervalSec = sec;
                    timer.Interval = sec * 1000;
                    gConns.IntervalSec = gHosts.IntervalSec = sec;
                    foreach (ToolStripMenuItem x in speedMenu.Items) x.Checked = x == mi;
                    btnSpeed.Text = "Speed: " + label + "  ▾";
                    UpdateStatus();
                };
                speedMenu.Items.Add(mi);
            }
        }

        static void InvalidateAll(Control c)
        {
            c.Invalidate();
            foreach (Control ch in c.Controls) InvalidateAll(ch);
        }

        Control Card(string title, Control inner)
        {
            var card = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(1, Theme.S(32), 1, 1), Margin = new Padding(Theme.S(5)) };
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                using (var br = new SolidBrush(Theme.Dim)) g.DrawString(title.ToUpperInvariant(), Theme.UIBold, br, Theme.S(12), Theme.S(9));
            };
            inner.Dock = DockStyle.Fill;
            card.Controls.Add(inner);
            return card;
        }

        void AddView(string name, Control c)
        {
            c.Dock = DockStyle.Fill;
            c.Visible = false;
            views[name] = c;
            content.Controls.Add(c);
        }

        void BuildViews()
        {
            // ---------------- Summary ----------------
            var summary = new TableLayoutPanel { BackColor = Theme.Bg, ColumnCount = 1, RowCount = 3, Padding = new Padding(0) };
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(112)));
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 48));

            var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(0) };
            for (int i = 0; i < 6; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
            tConns = new StatTile { Caption = "Active connections", Accent = Theme.Cyan };
            tListen = new StatTile { Caption = "Listening ports", Accent = Theme.Lime };
            tHosts = new StatTile { Caption = "Remote hosts now", Accent = Theme.Amber };
            tApps = new StatTile { Caption = "Apps on the network", Accent = Theme.Magenta };
            tDeps = new StatTile { Caption = "Dependencies recorded", Accent = Theme.Violet };
            tSamples = new StatTile { Caption = "Samples", Accent = Theme.Cyan };
            int col = 0;
            foreach (var t in new[] { tConns, tListen, tHosts, tApps, tDeps, tSamples })
            {
                t.Dock = DockStyle.Fill;
                t.Margin = new Padding(Theme.S(5));
                tiles.Controls.Add(t, col++, 0);
            }
            summary.Controls.Add(tiles, 0, 0);

            var graphs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(0) };
            graphs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            graphs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            gConns = new GlowGraph { Caption = "TCP connections", SeriesNames = new[] { "Total", "Inbound", "Outbound" }, Dock = DockStyle.Fill, Margin = new Padding(Theme.S(5)) };
            gConns.SeriesColors = new[] { Theme.Cyan, Theme.Magenta, Theme.Lime };
            gHosts = new GlowGraph { Caption = "Remote endpoints", SeriesNames = new[] { "Remote hosts", "Internet hosts", "Apps" }, Dock = DockStyle.Fill, Margin = new Padding(Theme.S(5)) };
            gHosts.SeriesColors = new[] { Theme.Amber, Theme.Red, Theme.Violet };
            graphs.Controls.Add(gConns, 0, 0);
            graphs.Controls.Add(gHosts, 1, 0);
            summary.Controls.Add(graphs, 0, 1);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            topApps = new VirtualList(new[] { "Application", "Conns", "Remote hosts", "Internet hosts", "Listening" }, new[] { 260, 70, 100, 110, 80 }, 1, false);
            topApps.CellColor = (r, c) => c == 1 ? Theme.Cyan : (c == 3 && (int)r.V[3] > 0 ? Theme.Amber : Color.Empty);
            topHosts = new VirtualList(new[] { "Remote host", "Scope", "Conns", "Apps" }, new[] { 280, 80, 70, 300 }, 2, false);
            topHosts.CellColor = (r, c) => c == 1 ? Theme.ScopeColor((string)r.V[1]) : (c == 2 ? Theme.Cyan : Color.Empty);
            bottom.Controls.Add(Card("Top applications right now", topApps.Grid), 0, 0);
            bottom.Controls.Add(Card("Top remote hosts right now", topHosts.Grid), 1, 0);
            summary.Controls.Add(bottom, 0, 2);
            AddView("Summary", summary);
            foreach (var vl in new[] { topApps, topHosts }) WireList(vl);

            // ---------------- Applications ----------------
            appsList = new VirtualList(
                new[] { "Application", "Status", "Conns now", "Listening", "Inbound clients", "Outbound hosts", "Internet hosts", "Top destinations", "Last seen", "PIDs", "Path" },
                new[] { 280, 70, 80, 80, 110, 110, 105, 360, 90, 90, 400 }, 0, true);
            appsList.CellColor = (r, c) =>
            {
                if (c == 1) return "Active".Equals(r.V[1]) ? Theme.Lime : Theme.Dim;
                if (c == 2) return Theme.Cyan;
                if (c == 4) return Theme.Magenta;
                if (c == 6 && r.V[6] is int && (int)r.V[6] > 0) return Theme.Amber;
                return Color.Empty;
            };
            AddView("Applications", Card("Applications", appsList.Grid));

            // ---------------- Connections (live) ----------------
            liveList = new VirtualList(
                new[] { "Process", "PID", "Protocol", "Local address", "Local port", "Remote address", "Remote port", "Remote host", "State", "Direction", "Scope", "Services", "Path" },
                new[] { 200, 60, 70, 140, 80, 150, 90, 240, 110, 95, 80, 220, 380 }, 0, true);
            liveList.CellColor = (r, c) =>
            {
                if (c == 8)
                {
                    string st = (string)r.V[8];
                    if (st == "ESTABLISHED") return Theme.Lime;
                    if (st == "LISTEN") return Theme.Cyan;
                    if (st.Length > 0) return Theme.Amber;
                }
                if (c == 9) return DirColor((string)r.V[9]);
                if (c == 10) return Theme.ScopeColor((string)r.V[10]);
                return Color.Empty;
            };
            AddView("Connections", Card("Live sockets", liveList.Grid));

            // ---------------- Dependencies ----------------
            depList = new VirtualList(
                new[] { "Active", "Application", "Direction", "Remote address", "Remote host", "Port", "Scope", "Conns now", "Max conc.", "Total conns", "Times seen", "First seen", "Last seen", "PIDs", "Path" },
                new[] { 60, 260, 90, 150, 240, 70, 80, 85, 85, 95, 95, 140, 140, 90, 380 }, 1, true);
            depList.CellColor = (r, c) =>
            {
                if (c == 0) return Theme.Lime;
                if (c == 2) return DirColor((string)r.V[2]);
                if (c == 5) return Theme.Cyan;
                if (c == 6) return Theme.ScopeColor((string)r.V[6]);
                return Color.Empty;
            };
            AddView("Dependencies", Card("Dependency history  ·  inbound port = this app's service port, outbound port = the remote service", depList.Grid));

            // ---------------- Listening ----------------
            lisList = new VirtualList(
                new[] { "Active", "Application", "Protocol", "Address", "Port", "Exposure", "First seen", "Last seen", "Times seen", "PIDs", "Path" },
                new[] { 60, 280, 80, 160, 70, 130, 140, 140, 95, 90, 400 }, 4, true);
            lisList.CellColor = (r, c) =>
            {
                if (c == 0) return Theme.Lime;
                if (c == 4) return Theme.Cyan;
                if (c == 5) return "All interfaces".Equals(r.V[5]) ? Theme.Amber : ("Loopback only".Equals(r.V[5]) ? Theme.Dim : Theme.Text);
                return Color.Empty;
            };
            AddView("Listening", Card("Listening ports  ·  \"All interfaces\" = reachable from the network (subject to firewall)", lisList.Grid));

            // ---------------- Remote hosts ----------------
            hostList = new VirtualList(
                new[] { "Remote address", "Host name", "Scope", "Status", "Conns now", "Direction", "Local apps", "Ports", "Total conns", "First seen", "Last seen" },
                new[] { 160, 260, 80, 70, 85, 90, 360, 200, 95, 140, 140 }, 0, true);
            hostList.CellColor = (r, c) =>
            {
                if (c == 2) return Theme.ScopeColor((string)r.V[2]);
                if (c == 3) return "Active".Equals(r.V[3]) ? Theme.Lime : Theme.Dim;
                if (c == 4) return Theme.Cyan;
                if (c == 5) return DirColor((string)r.V[5]);
                return Color.Empty;
            };
            AddView("Remote Hosts", Card("Remote hosts", hostList.Grid));

            foreach (var vl in new[] { appsList, liveList, depList, lisList, hostList }) WireList(vl);

            // ---------------- Map ----------------
            var mapView = new Panel { BackColor = Theme.Bg };
            map = new MapPanel { Dock = DockStyle.Fill };
            var mapCard = Card("Dependency map", map);
            mapList = new ListBox
            {
                Dock = DockStyle.Fill, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(26), IntegralHeight = false, Font = Theme.UI
            };
            mapList.HandleCreated += (s, e) => Theme.DarkScrollbars(mapList);
            mapList.DrawItem += MapList_DrawItem;
            mapList.SelectedIndexChanged += (s, e) => { if (!updatingMapList) UpdateMap(); };
            var listCard = Card("Applications", mapList);
            listCard.Dock = DockStyle.Left;
            listCard.Width = Theme.S(290);
            mapView.Controls.Add(mapCard);
            mapView.Controls.Add(listCard);
            AddView("Map", mapView);
        }

        static Color DirColor(string d)
        {
            if (d == "Inbound") return Theme.Magenta;
            if (d == "Outbound") return Theme.Cyan;
            if (d == "In + Out") return Theme.Violet;
            if (d == "Listening") return Theme.Lime;
            return Theme.Dim;
        }

        void WireList(VirtualList vl)
        {
            var cm = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.UI };
            cm.Items.Add("Show on map", null, (s, e) => ShowSelectedOnMap(vl));
            cm.Items.Add("Copy selected rows", null, (s, e) => vl.CopySelected());
            cm.Items.Add("Filter to this application", null, (s, e) =>
            {
                var sel = vl.SelectedRows();
                if (sel.Count > 0 && sel[0].App != null) txtFilter.Text = sel[0].App;
            });
            vl.Grid.ContextMenuStrip = cm;
            vl.Grid.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !vl.Grid.Rows[e.RowIndex].Selected)
                {
                    vl.Grid.ClearSelection();
                    vl.Grid.Rows[e.RowIndex].Selected = true;
                }
            };
            vl.Grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowSelectedOnMap(vl); };
        }

        // =====================================================================
        //  Painting of chrome
        // =====================================================================
        void Brand_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Theme.Scale;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // logo: concentric rings
            float cx = 30 * s, cy = 37 * s;
            using (var pen = new Pen(Theme.Cyan, 2 * s)) g.DrawEllipse(pen, cx - 11 * s, cy - 11 * s, 22 * s, 22 * s);
            using (var pen = new Pen(Theme.Magenta, 2 * s)) g.DrawArc(pen, cx - 6 * s, cy - 6 * s, 12 * s, 12 * s, 200, 230);
            using (var br = new SolidBrush(Theme.Lime)) g.FillEllipse(br, cx - 2.5f * s, cy - 2.5f * s, 5 * s, 5 * s);
            Theme.GlowText(g, "PORTS", Theme.Brand, Theme.Cyan, new PointF(50 * s, 20 * s));
            Theme.GlowText(g, "IN USE", Theme.Brand, Theme.Magenta, new PointF(50 * s, 38 * s));
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 14 * s, brand.Height - 1, brand.Width - 14 * s, brand.Height - 1);
        }

        void SideStats_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Theme.Scale;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 14 * s, 0, sideStats.Width - 14 * s, 0);
            float y = 12 * s;
            using (var dim = new SolidBrush(Theme.Dim))
            using (var txt = new SolidBrush(Theme.Text))
            using (var amber = new SolidBrush(Theme.Amber))
            {
                string[][] lines =
                {
                    new[] { "Host", Environment.MachineName },
                    new[] { "Elevated", elevated ? "Yes" : "No" },
                    new[] { "Since", model.FirstSample == DateTime.MinValue ? "—" : Fmt.Time(model.FirstSample) },
                    new[] { "Interval", intervalSec + " s" },
                };
                foreach (var l in lines)
                {
                    g.DrawString(l[0].ToUpperInvariant(), Theme.Small, dim, 18 * s, y);
                    g.DrawString(l[1], Theme.Mono, l[0] == "Elevated" && !elevated ? amber : txt, 86 * s, y - 1 * s);
                    y += 22 * s;
                }
            }
        }

        void Header_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            float s = Theme.Scale;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = 16 * s;
            using (var br = new SolidBrush(Theme.Text)) g.DrawString(currentView, Theme.Title, br, x, 10 * s);
            float tw = g.MeasureString(currentView, Theme.Title).Width;

            // LIVE / PAUSED pill
            string pill = running ? "LIVE" : "PAUSED";
            Color pc = running ? Theme.Lime : Theme.Amber;
            var pr = new RectangleF(x + tw + 10 * s, 17 * s, g.MeasureString(pill, Theme.Small).Width + 26 * s, 20 * s);
            using (var path = Theme.Round(pr, 10 * s))
            {
                using (var br = new SolidBrush(Theme.A(pc, 30))) g.FillPath(br, path);
                using (var pen = new Pen(Theme.A(pc, 150))) g.DrawPath(pen, path);
            }
            float d = 7 * s;
            if (running && Theme.Effects && Animator.Active)
            {
                float phase = (float)(0.5 + 0.5 * Math.Sin(Animator.Now / 1.6 * Math.PI * 2));
                float gr = d + 8 * s * phase;
                using (var br = new SolidBrush(Theme.A(pc, (int)(90 * (1 - phase)))))
                    g.FillEllipse(br, pr.X + 10 * s + d / 2 - gr / 2, pr.Y + pr.Height / 2 - gr / 2, gr, gr);
            }
            using (var br = new SolidBrush(pc)) g.FillEllipse(br, pr.X + 10 * s, pr.Y + (pr.Height - d) / 2, d, d);
            using (var br = new SolidBrush(pc)) g.DrawString(pill, Theme.Small, br, pr.X + 20 * s, pr.Y + 3 * s);

            string desc;
            descriptions.TryGetValue(currentView, out desc);
            using (var br = new SolidBrush(Theme.Dim)) g.DrawString(desc ?? "", Theme.UI, br, x + 1, 42 * s);
        }

        void MapList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            float s = Theme.Scale;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var br = new SolidBrush(sel ? Theme.PanelHi : Theme.Panel)) g.FillRectangle(br, e.Bounds);
            if (sel) using (var br = new SolidBrush(Theme.Magenta)) g.FillRectangle(br, e.Bounds.X, e.Bounds.Y + 4, Theme.S(3), e.Bounds.Height - 8);
            string text = (string)mapList.Items[e.Index];
            bool active;
            mapAppActive.TryGetValue(text, out active);
            if (text == WholePc) active = true;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = 7 * s;
            using (var br = new SolidBrush(active ? Theme.Lime : Theme.Border))
                g.FillEllipse(br, e.Bounds.X + 10 * s, e.Bounds.Y + (e.Bounds.Height - d) / 2, d, d);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var br = new SolidBrush(sel ? Color.White : (active ? Theme.Text : Theme.Dim)))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, text == WholePc ? Theme.UIBold : Theme.UI, br, new RectangleF(e.Bounds.X + 24 * s, e.Bounds.Y, e.Bounds.Width - 28 * s, e.Bounds.Height), sf);
        }

        // =====================================================================
        //  Navigation
        // =====================================================================
        void ShowView(string name)
        {
            currentView = name;
            foreach (var kv in nav) kv.Value.Selected = kv.Key == name;
            foreach (var kv in views) kv.Value.Visible = kv.Key == name;
            btnMap.Visible = name != "Map" && name != "Summary";
            chipEph.Visible = name == "Listening" || name == "Connections" || name == "Map";
            header.Invalidate();
            RefreshCurrentView();
        }

        void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { e.Handled = true; Sample(); }
            else if (e.Control && e.KeyCode == Keys.F) { e.Handled = true; txtFilter.Focus(); txtFilter.SelectAll(); }
            else if (e.KeyCode == Keys.Escape && txtFilter.Focused) { txtFilter.Text = ""; }
            else if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D7)
            {
                var names = new[] { "Summary", "Applications", "Connections", "Dependencies", "Listening", "Remote Hosts", "Map" };
                ShowView(names[e.KeyCode - Keys.D1]);
                e.Handled = true;
            }
        }

        // =====================================================================
        //  Sampling
        // =====================================================================
        void StartMonitoring()
        {
            running = true;
            timer.Start();
            gConns.Freeze(false); gHosts.Freeze(false);
            btnPause.Text = "❚❚  Pause";
            btnPause.ForeColor = Theme.Amber;
            btnPause.FlatAppearance.BorderColor = Theme.A(Theme.Amber, 160);
            header.Invalidate();
            Sample();
        }

        void StopMonitoring()
        {
            running = false;
            timer.Stop();
            gConns.Freeze(true); gHosts.Freeze(true);
            btnPause.Text = "▶  Resume";
            btnPause.ForeColor = Theme.Lime;
            btnPause.FlatAppearance.BorderColor = Theme.A(Theme.Lime, 160);
            header.Invalidate();
            UpdateStatus();
        }

        void Timer_Tick(object sender, EventArgs e) { Sample(); }

        // =====================================================================
        //  60 fps animation (vsync-paced; see Animator)
        // =====================================================================
        void UpdateAnimatorState()
        {
            bool on = Theme.Effects && miSmooth.Checked && WindowState != FormWindowState.Minimized && Visible;
            if (on != Animator.Active)
            {
                Animator.SetEnabled(on);
                InvalidateAll(this);
            }
        }

        void OnFrame(double dt)
        {
            frameCount++;
            // LIVE pulse in the header
            if (running) Animator.Repaint(header, new Rectangle(0, 0, Theme.S(420), Theme.S(42)));

            if (currentView == "Summary")
            {
                foreach (var t in new[] { tConns, tListen, tHosts, tApps, tDeps, tSamples })
                    if (t.Step(dt)) Animator.Repaint(t);
                if (running) { Animator.Repaint(gConns); Animator.Repaint(gHosts); }
            }
            else if (currentView == "Map" && map.HasActiveLinks)
            {
                // big maps are expensive in GDI+: drop to every 2nd/3rd frame if a paint costs > 8 ms
                int skip = Math.Max(1, Math.Min(4, (int)Math.Ceiling(map.LastPaintMs / 8.0)));
                if (frameCount % skip == 0) Animator.Repaint(map);
            }
        }

        async void Sample()
        {
            if (busy) return;
            busy = true;
            try
            {
                var snap = await Task.Run(() => Collector.Take(resolver));
                model.Ingest(snap);
                UpdateSummaryMetrics();
                RefreshCurrentView();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                statusBar.Text = "   Sampling error: " + ex.Message;
            }
            finally { busy = false; }
        }

        // =====================================================================
        //  Filtering helpers
        // =====================================================================
        string[] Terms()
        {
            return txtFilter.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        static bool Match(string[] terms, string text)
        {
            foreach (var t in terms)
                if (text.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        List<Row> TextFilter(List<Row> rows, out int total)
        {
            total = rows.Count;
            var terms = Terms();
            if (terms.Length == 0) return rows;
            return rows.Where(r => Match(terms, string.Join(" ", r.V.Select(v => v is DateTime || v is bool ? "" : VirtualList.Text(v))))).ToList();
        }

        bool PassDep(DepRecord d)
        {
            if (chipLoop.Checked && d.Scope == "Loopback") return false;
            if (chipActive.Checked && !d.Active) return false;
            return true;
        }

        bool PassLis(ListenRecord l)
        {
            if (chipLoop.Checked && l.Exposure == "Loopback only") return false;
            if (chipActive.Checked && !l.Active) return false;
            if (chipEph.Checked && l.Port >= 49152) return false;
            return true;
        }

        static string HostLabel(string ip)
        {
            string h = DnsCache.Get(ip);
            return h.Length > 0 ? h + " (" + ip + ")" : ip;
        }

        void SetList(VirtualList vl, List<Row> rows)
        {
            int total;
            var shown = TextFilter(rows, out total);
            vl.SetRows(shown);
            lblShowing.Text = shown.Count == total ? total + " rows" : "showing " + shown.Count + " of " + total;
        }

        // =====================================================================
        //  View refresh
        // =====================================================================
        void RefreshCurrentView()
        {
            if (model.Last == null && model.Deps.Count == 0) { lblShowing.Text = ""; return; }
            switch (currentView)
            {
                case "Summary": UpdateSummaryLists(); break;
                case "Applications": UpdateApps(); break;
                case "Connections": UpdateLive(); break;
                case "Dependencies": UpdateDeps(); break;
                case "Listening": UpdateListeners(); break;
                case "Remote Hosts": UpdateHosts(); break;
                case "Map": UpdateMapList(); UpdateMap(); lblShowing.Text = ""; break;
            }
        }

        void UpdateSummaryMetrics()
        {
            var snap = model.Last;
            if (snap == null) return;
            var conns = snap.Entries.Where(TrafficModel.IsConnection).ToList();
            int inbound = conns.Count(e => model.CurrentListenPorts.Contains(e.LocalPort));
            int outbound = conns.Count - inbound;
            var remotes = conns.Select(e => e.RemoteAddress).Where(a => !Fmt.IsLoopback(a)).Select(Fmt.Addr).Distinct().ToList();
            int internet = conns.Where(e => Fmt.Scope(e.RemoteAddress) == "Internet").Select(e => Fmt.Addr(e.RemoteAddress)).Distinct().Count();
            int lan = remotes.Count - internet;
            int apps = conns.Select(e => TrafficModel.Proc(snap, e.Pid).AppName).Distinct().Count();
            int listenTcp = snap.Entries.Count(e => e.IsTcp && e.State == Native.TCP_LISTEN);
            int udp = snap.Entries.Count(e => !e.IsTcp);
            int exposed = snap.Entries.Count(e => e.IsTcp && e.State == Native.TCP_LISTEN && Fmt.IsAny(e.LocalAddress));

            tConns.Set(conns.Count.ToString(), inbound + " in  ·  " + outbound + " out", conns.Count);
            tListen.Set(listenTcp.ToString(), exposed + " on all interfaces  ·  " + udp + " UDP", listenTcp);
            tHosts.Set(remotes.Count.ToString(), internet + " internet  ·  " + lan + " local", remotes.Count);
            tApps.Set(apps.ToString(), "with live TCP connections", apps);
            tDeps.Set(model.Deps.Count.ToString(), model.Listeners.Count + " listener records", model.Deps.Count);
            tSamples.Set(model.Samples.ToString(), "every " + intervalSec + " s  ·  " + snap.ElapsedMs + " ms", snap.ElapsedMs);

            gConns.AddPoint(new float[] { conns.Count, inbound, outbound });
            gHosts.AddPoint(new float[] { remotes.Count, internet, apps });

            nav["Connections"].SetBadge(conns.Count.ToString());
            nav["Dependencies"].SetBadge(model.Deps.Count.ToString());
            nav["Listening"].SetBadge(listenTcp.ToString());
            nav["Remote Hosts"].SetBadge(remotes.Count.ToString());
            nav["Applications"].SetBadge(apps.ToString());
            sideStats.Invalidate();
        }

        void UpdateSummaryLists()
        {
            var snap = model.Last;
            if (snap == null) return;
            var terms = Terms();
            var conns = snap.Entries.Where(TrafficModel.IsConnection)
                .Where(e => !chipLoop.Checked || !Fmt.IsLoopback(e.RemoteAddress)).ToList();
            var listen = snap.Entries.Where(e => e.IsTcp && e.State == Native.TCP_LISTEN).ToList();

            var appRows = conns.GroupBy(e => TrafficModel.Proc(snap, e.Pid).AppName).Select(g => new Row
            {
                Key = g.Key, App = g.Key,
                V = new object[]
                {
                    g.Key, g.Count(),
                    g.Select(e => Fmt.Addr(e.RemoteAddress)).Distinct().Count(),
                    g.Where(e => Fmt.Scope(e.RemoteAddress) == "Internet").Select(e => Fmt.Addr(e.RemoteAddress)).Distinct().Count(),
                    listen.Count(l => TrafficModel.Proc(snap, l.Pid).AppName == g.Key)
                }
            }).Where(r => Match(terms, (string)r.V[0])).ToList();
            topApps.SetRows(appRows);

            var hostRows = conns.GroupBy(e => Fmt.Addr(e.RemoteAddress)).Select(g =>
            {
                var apps = g.Select(e => TrafficModel.Proc(snap, e.Pid).AppName).Distinct().ToList();
                return new Row
                {
                    Key = g.Key, App = apps.Count == 1 ? apps[0] : null,
                    V = new object[] { HostLabel(g.Key), Fmt.Scope(g.First().RemoteAddress), g.Count(), Fmt.JoinLimited(apps, 3) }
                };
            }).Where(r => Match(terms, (string)r.V[0] + " " + r.V[3])).ToList();
            topHosts.SetRows(hostRows);
            lblShowing.Text = "";
        }

        void UpdateApps()
        {
            var deps = model.Deps.Values.Where(PassDep).ToList();
            var lis = model.Listeners.Values.Where(PassLis).ToList();
            var depBy = deps.GroupBy(d => d.App).ToDictionary(g => g.Key, g => g.ToList());
            var lisBy = lis.GroupBy(l => l.App).ToDictionary(g => g.Key, g => g.ToList());
            var rows = new List<Row>();
            foreach (var app in depBy.Keys.Union(lisBy.Keys))
            {
                List<DepRecord> ad;
                if (!depBy.TryGetValue(app, out ad)) ad = new List<DepRecord>();
                List<ListenRecord> al;
                if (!lisBy.TryGetValue(app, out al)) al = new List<ListenRecord>();
                bool active = ad.Any(d => d.Active) || al.Any(l => l.Active);
                var outb = ad.Where(d => !d.Inbound).ToList();
                var top = outb.GroupBy(d => d.RemoteAddress).OrderByDescending(g => g.Sum(d => d.TotalConnections))
                    .Select(g => { string h = DnsCache.Get(g.Key); return (h.Length > 0 ? h : g.Key) + ":" + string.Join("/", g.Select(d => d.RemotePort).Distinct().OrderBy(p => p)); });
                DateTime last = ad.Select(d => d.LastSeen).Concat(al.Select(l => l.LastSeen)).DefaultIfEmpty(DateTime.MinValue).Max();
                var pids = ad.SelectMany(d => d.Pids).Concat(al.SelectMany(l => l.Pids)).Distinct().OrderBy(p => p);
                string path = ad.Select(d => d.ProcessPath).Concat(al.Select(l => l.ProcessPath)).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
                rows.Add(new Row
                {
                    Key = app, App = app, Active = active,
                    V = new object[]
                    {
                        app, active ? "Active" : "Idle",
                        ad.Sum(d => d.CurrentCount),
                        al.Count(l => l.Active),
                        ad.Where(d => d.Inbound).Select(d => d.RemoteAddress).Distinct().Count(),
                        outb.Select(d => d.RemoteAddress).Distinct().Count(),
                        ad.Where(d => d.Scope == "Internet").Select(d => d.RemoteAddress).Distinct().Count(),
                        Fmt.JoinLimited(top, 3),
                        last, Fmt.JoinLimited(pids, 6), path
                    }
                });
            }
            SetList(appsList, rows);
        }

        void UpdateLive()
        {
            var snap = model.Last;
            if (snap == null) return;
            var rows = new List<Row>(snap.Entries.Count);
            foreach (var e in snap.Entries)
            {
                bool listen = TrafficModel.IsListener(e);
                if (chipLoop.Checked && (listen ? Fmt.IsLoopback(e.LocalAddress) : Fmt.IsLoopback(e.RemoteAddress))) continue;
                if (chipEph.Checked && listen && e.LocalPort >= 49152) continue;
                bool isConn = e.IsTcp && !listen && !Fmt.IsAny(e.RemoteAddress);
                if (chipActive.Checked && e.IsTcp && !listen && e.State != 5) continue;
                var pi = TrafficModel.Proc(snap, e.Pid);
                string la = Fmt.Addr(e.LocalAddress);
                string ra = isConn ? Fmt.Addr(e.RemoteAddress) : "";
                rows.Add(new Row
                {
                    Key = e.Protocol + "|" + la + "|" + e.LocalPort + "|" + ra + "|" + e.RemotePort + "|" + e.Pid,
                    App = pi.AppName,
                    V = new object[]
                    {
                        pi.Name, e.Pid, e.Protocol, la, e.LocalPort,
                        ra, isConn ? (object)e.RemotePort : null,
                        isConn ? DnsCache.Get(ra) : "",
                        e.IsTcp ? Native.StateName(e.State) : "",
                        model.DirectionOf(e),
                        isConn ? Fmt.Scope(e.RemoteAddress) : "",
                        pi.Services ?? "", pi.Path ?? ""
                    }
                });
            }
            SetList(liveList, rows);
        }

        void UpdateDeps()
        {
            var rows = model.Deps.Values.Where(PassDep).Select(d => new Row
            {
                Key = d.Key, App = d.App, Active = d.Active,
                V = new object[]
                {
                    d.Active, d.App, d.Direction, d.RemoteAddress, DnsCache.Get(d.RemoteAddress), d.ServicePort, d.Scope,
                    d.CurrentCount, d.MaxConcurrent, d.TotalConnections, d.TimesSeen, d.FirstSeen, d.LastSeen,
                    string.Join(", ", d.Pids.OrderBy(p => p)), d.ProcessPath ?? ""
                }
            }).ToList();
            SetList(depList, rows);
        }

        void UpdateListeners()
        {
            var rows = model.Listeners.Values.Where(PassLis).Select(l => new Row
            {
                Key = l.Key, App = l.App, Active = l.Active,
                V = new object[]
                {
                    l.Active, l.App, l.Protocol, l.LocalAddress, l.Port, l.Exposure, l.FirstSeen, l.LastSeen, l.TimesSeen,
                    string.Join(", ", l.Pids.OrderBy(p => p)), l.ProcessPath ?? ""
                }
            }).ToList();
            SetList(lisList, rows);
        }

        void UpdateHosts()
        {
            var rows = model.Deps.Values.Where(PassDep).GroupBy(d => d.RemoteAddress).Select(g =>
            {
                bool active = g.Any(d => d.Active);
                bool hasIn = g.Any(d => d.Inbound), hasOut = g.Any(d => !d.Inbound);
                var apps = g.Select(d => d.App).Distinct().OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
                return new Row
                {
                    Key = g.Key, App = apps.Count == 1 ? apps[0] : null, Active = active,
                    V = new object[]
                    {
                        g.Key, DnsCache.Get(g.Key), g.First().Scope, active ? "Active" : "Idle",
                        g.Sum(d => d.CurrentCount),
                        hasIn && hasOut ? "In + Out" : (hasIn ? "Inbound" : "Outbound"),
                        Fmt.JoinLimited(apps, 4),
                        Fmt.JoinLimited(g.Select(d => d.ServicePort).Distinct().OrderBy(p => p), 8),
                        g.Sum(d => d.TotalConnections),
                        g.Min(d => d.FirstSeen), g.Max(d => d.LastSeen)
                    }
                };
            }).ToList();
            SetList(hostList, rows);
        }

        // ---------------- map ----------------
        void UpdateMapList()
        {
            var terms = Terms();
            var deps = model.Deps.Values.Where(PassDep).ToList();
            var lis = model.Listeners.Values.Where(PassLis).ToList();
            mapAppActive.Clear();
            foreach (var d in deps) { bool a; mapAppActive.TryGetValue(d.App, out a); mapAppActive[d.App] = a || d.Active; }
            foreach (var l in lis) { bool a; mapAppActive.TryGetValue(l.App, out a); mapAppActive[l.App] = a || l.Active; }

            var apps = new List<string> { WholePc };
            apps.AddRange(mapAppActive.Keys.Where(a => Match(terms, a)).OrderBy(a => a, StringComparer.OrdinalIgnoreCase));
            if (!apps.SequenceEqual(mapList.Items.Cast<string>()))
            {
                string sel = mapList.SelectedItem as string;
                updatingMapList = true;
                mapList.BeginUpdate();
                mapList.Items.Clear();
                mapList.Items.AddRange(apps.ToArray());
                int i = sel == null ? 0 : mapList.Items.IndexOf(sel);
                mapList.SelectedIndex = i >= 0 ? i : 0;
                mapList.EndUpdate();
                updatingMapList = false;
            }
            else mapList.Invalidate();
        }

        void UpdateMap()
        {
            string app = mapList.SelectedItem as string ?? WholePc;
            bool whole = app == WholePc;
            var deps = model.Deps.Values.Where(PassDep).Where(d => whole || d.App == app).ToList();
            var lis = model.Listeners.Values.Where(PassLis).Where(l => whole || l.App == app).ToList();
            var md = new MapData();
            if (whole)
            {
                md.Center = Environment.MachineName;
                md.CenterSub = "This computer  ·  " + deps.Select(d => d.App).Distinct().Count() + " apps with TCP dependencies";
            }
            else
            {
                md.Center = app;
                string path = deps.Select(d => d.ProcessPath).Concat(lis.Select(l => l.ProcessPath)).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
                var pids = deps.SelectMany(d => d.Pids).Concat(lis.SelectMany(l => l.Pids)).Distinct().OrderBy(p => p);
                md.CenterSub = (path.Length > 0 ? path + "\n" : "") + "PID " + Fmt.JoinLimited(pids, 6);
            }
            var lports = lis.Where(l => l.Active || !chipActive.Checked)
                .Select(l => (l.IsTcp ? "TCP " : "UDP ") + l.Port).Distinct()
                .OrderBy(p => p.StartsWith("UDP")).ThenBy(p => int.Parse(p.Substring(4))).ToList();
            if (lports.Count > 0) md.CenterDetail = "LISTENING  " + Fmt.JoinLimited(lports, whole ? 14 : 30);

            md.Inbound = deps.Where(d => d.Inbound).GroupBy(d => d.RemoteAddress).OrderBy(g => Fmt.IpKey(g.Key))
                .Select(g => MakeNode(g.ToList(), true, whole)).ToList();
            md.Outbound = deps.Where(d => !d.Inbound).GroupBy(d => d.RemoteAddress)
                .OrderBy(g => g.First().Scope == "Internet" ? 1 : 0).ThenBy(g => Fmt.IpKey(g.Key))
                .Select(g => MakeNode(g.ToList(), false, whole)).ToList();
            map.SetData(md);
        }

        static MapNode MakeNode(List<DepRecord> g, bool inbound, bool whole)
        {
            string ip = g[0].RemoteAddress;
            string host = DnsCache.Get(ip);
            var ports = g.Select(d => d.ServicePort).Distinct().OrderBy(p => p);
            string detail = (inbound ? "→ :" : "tcp/") + Fmt.JoinLimited(ports, 6);
            if (whole) detail += "  ·  " + Fmt.JoinLimited(g.Select(d => d.App).Distinct(), 2);
            long conns = g.Sum(d => d.TotalConnections);
            return new MapNode
            {
                Title = host.Length > 0 ? host : ip,
                Sub = (host.Length > 0 ? ip + "  ·  " : "") + g[0].Scope + "  ·  " + conns + " conns  ·  last " + Fmt.Time(g.Max(d => d.LastSeen)),
                Detail = detail,
                Scope = g[0].Scope,
                Active = g.Any(d => d.Active)
            };
        }

        void ShowSelectedOnMap() { ShowSelectedOnMap(CurrentList()); }

        void ShowSelectedOnMap(VirtualList vl)
        {
            string app = null;
            if (vl != null)
            {
                var sel = vl.SelectedRows();
                if (sel.Count > 0) app = sel[0].App;
            }
            ShowView("Map");
            if (app == null) return;
            int i = mapList.Items.IndexOf(app);
            if (i < 0 && txtFilter.Text.Length > 0) { txtFilter.Text = ""; i = mapList.Items.IndexOf(app); }
            if (i >= 0) mapList.SelectedIndex = i;
        }

        VirtualList CurrentList()
        {
            switch (currentView)
            {
                case "Applications": return appsList;
                case "Connections": return liveList;
                case "Dependencies": return depList;
                case "Listening": return lisList;
                case "Remote Hosts": return hostList;
                case "Summary": return topApps;
                default: return null;
            }
        }

        void UpdateStatus()
        {
            string last = model.LastSample == DateTime.MinValue ? "—" : model.LastSample.ToString("HH:mm:ss");
            string since = model.FirstSample == DateTime.MinValue ? "—" : model.FirstSample.ToString("yyyy-MM-dd HH:mm");
            statusBar.Text = (running ? "● Monitoring" : "■ Paused") +
                "     Last sample " + last + (model.Last != null ? " (" + model.Last.ElapsedMs + " ms)" : "") +
                "     Every " + intervalSec + " s     Recording since " + since +
                "     " + model.Samples + " samples" +
                (miAutosave.Checked ? "     Auto-save → " + AutoSavePath() : "") +
                (Animator.Active && Animator.Fps > 0 ? "     " + Animator.Fps.ToString("0") + " fps · " + Animator.FrameMs.ToString("0.0") + " ms/frame" : "") +
                (elevated ? "" : "     Tip: run as Administrator to see owners of all system processes");
            sideStats.Invalidate();
        }

        // =====================================================================
        //  Files
        // =====================================================================
        static string AutoSavePath()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string test = Path.Combine(dir, ".writetest");
                File.WriteAllText(test, "");
                File.Delete(test);
                return Path.Combine(dir, "PortsInUse-autosave.tsv");
            }
            catch
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortsInUse");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "PortsInUse-autosave.tsv");
            }
        }

        void AutoSave()
        {
            try { model.Save(sessionPath ?? AutoSavePath()); }
            catch (Exception ex) { statusBar.Text = "   Auto-save failed: " + ex.Message; }
        }

        void SaveSession()
        {
            using (var d = new SaveFileDialog { Filter = "Ports In Use session (*.tsv)|*.tsv", FileName = "PortsInUse-" + Environment.MachineName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".tsv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { model.Save(d.FileName); sessionPath = d.FileName; UpdateStatus(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void LoadSession()
        {
            using (var d = new OpenFileDialog { Filter = "Ports In Use session (*.tsv)|*.tsv|All files|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int n = model.Load(d.FileName);
                    RefreshCurrentView();
                    MessageBox.Show(this, n + " records merged into the current history.", "Session loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void ExportCsv()
        {
            var vl = CurrentList();
            if (vl == null || currentView == "Summary") vl = depList;
            if (vl == depList) UpdateDeps();
            using (var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "PortsInUse-" + (vl == depList ? "Dependencies" : currentView.Replace(" ", "")) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { vl.ExportCsv(d.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void SaveMapPng()
        {
            if (currentView != "Map") ShowView("Map");
            using (var d = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "PortsInUse-Map-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".png" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { using (var bmp = map.RenderToBitmap()) bmp.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void ClearHistory()
        {
            if (MessageBox.Show(this, "Clear all recorded history and start a new session?", "New session", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            model.Clear();
            gConns.Clear(); gHosts.Clear();
            foreach (var t in new[] { tConns, tListen, tHosts, tApps, tDeps, tSamples }) t.ClearSpark();
            sessionPath = null;
            Sample();
        }

        void RestartElevated()
        {
            try
            {
                if (miAutosave.Checked) AutoSave();
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch (Win32Exception) { /* user cancelled UAC */ }
        }

        void ShowAbout()
        {
            MessageBox.Show(this,
                "Ports In Use — portable network dependency monitor\n\n" +
                "Samples the Windows TCP/UDP tables (the same data as netstat -ano) on a timer, " +
                "attributes every socket to its process (and hosted services for svchost), and builds a " +
                "history of which applications talk to which remote devices and ports — in the spirit of " +
                "Microsoft's Application Dependency Mapper.\n\n" +
                "Direction: a connection is Inbound when its local port is a listening port on this PC, otherwise Outbound.\n" +
                "Connections shorter than the sample interval can be missed; use Speed: High to catch more.\n\n" +
                "Shortcuts: Ctrl+1…7 switch views · Ctrl+F filter · F5 sample now · Ctrl+C copy rows · double-click a row to map it.",
                "About Ports In Use", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
    }
}
