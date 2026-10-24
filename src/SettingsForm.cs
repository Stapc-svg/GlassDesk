// GlassDesk - the settings window
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace GlassDesk
{
    internal class SettingsForm : Form
    {
        readonly App app;
        Palette P;
        float k;
        Panel side, content;
        readonly List<NavItem> navs = new List<NavItem>();
        int page;
        Font fBody, fSmall, fH1, fSection, fGlyph, fBig;
        Timer perfTimer;
        Label perfRam, perfCpu, perfInfo;
        TimeSpan lastCpu; DateTime lastAt;

        static readonly string[][] Pages =
        {
            new[] { "", "General" }, new[] { "", "Appearance" }, new[] { "", "Widgets" },
            new[] { "", "Organizer" }, new[] { "", "Performance" }, new[] { "", "About" },
        };

        Settings S { get { return app.Settings; } }

        public SettingsForm(App app, int startPage)
        {
            this.app = app; page = startPage;
            Text = "GlassDesk Settings";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Icon = app.AppIcon;
            ShowInTaskbar = true;
            perfTimer = new Timer { Interval = 2000 };
            perfTimer.Tick += delegate { UpdatePerf(); };
            Build();
            Load += delegate { Native.SetDarkTitleBar(Handle, P.Dark); Native.SetRoundCorners(Handle); };
            FormClosed += delegate { perfTimer.Stop(); perfTimer.Dispose(); Gfx.ClearFonts(); if (app.Settings.TrimMemory) Native.TrimMemory(); };
        }

        public int CurrentPage { get { return page; } }

        /// <summary>Rebuild colors after the theme or accent changed (deferred so the clicked control is not disposed mid-click).</summary>
        public void RestyleSoon()
        {
            if (IsHandleCreated) BeginInvoke(new Action(delegate { forceRebuild = true; keepScroll = true; Build(); }));
        }

        public void Build()
        {
            SuspendLayout();
            foreach (Control c in Controls) c.Dispose();
            Controls.Clear(); navs.Clear();
            P = Palette.From(app.Theme);
            k = app.DpiScale;
            string ui = Theme.Pick("Segoe UI", "DejaVu Sans");
            string semi = Theme.Pick("Segoe UI Semibold", "Segoe UI", "DejaVu Sans");
            string glyph = Theme.Pick("Segoe Fluent Icons", "Segoe MDL2 Assets", "");
            fBody = Gfx.SafeFont(ui, 14 * k, FontStyle.Regular);
            fSmall = Gfx.SafeFont(ui, 12.5f * k, FontStyle.Regular);
            fSection = Gfx.SafeFont(semi, 13 * k, FontStyle.Regular);
            fH1 = Gfx.SafeFont(semi, 27 * k, FontStyle.Regular);
            fBig = Gfx.SafeFont(semi, 30 * k, FontStyle.Regular);
            fGlyph = glyph.Length > 0 ? Gfx.SafeFont(glyph, 16 * k, FontStyle.Regular) : null;
            Font = fBody;
            BackColor = P.Bg; ForeColor = P.Text;
            ClientSize = new Size((int)(860 * k), (int)(600 * k));
            MinimumSize = new Size((int)(700 * k), (int)(460 * k));

            content = new DoubleBufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = P.Bg };
            side = new Panel { Dock = DockStyle.Left, Width = (int)(218 * k), BackColor = P.Side };
            Controls.Add(content); Controls.Add(side);

            var logo = new LogoBox(app, k) { Location = new Point((int)(18 * k), (int)(20 * k)), BackColor = P.Side };
            side.Controls.Add(logo);
            var name = new Label { Text = "GlassDesk", Font = Gfx.SafeFont(semi, 17 * k, FontStyle.Regular), ForeColor = P.Text, AutoSize = true, Location = new Point((int)(60 * k), (int)(22 * k)), BackColor = P.Side };
            var ver = new Label { Text = "Desktop organizer", Font = fSmall, ForeColor = P.Sub, AutoSize = true, Location = new Point((int)(61 * k), (int)(45 * k)), BackColor = P.Side };
            side.Controls.Add(name); side.Controls.Add(ver);
            int y = (int)(92 * k);
            for (int i = 0; i < Pages.Length; i++)
            {
                var n = new NavItem(P, k, Pages[i][0], Pages[i][1], fGlyph) { Location = new Point((int)(16 * k), y), Font = fBody, BackColor = P.Side };
                int idx = i;
                n.Click += delegate { ShowPage(idx); };
                navs.Add(n); side.Controls.Add(n);
                y += (int)(44 * k);
            }
            var hint = new Label { Text = "Ctrl + Alt + G\nshow / hide panels", Font = fSmall, ForeColor = P.Sub, AutoSize = true, BackColor = P.Side };
            hint.Location = new Point((int)(30 * k), ClientSize.Height - (int)(64 * k));
            hint.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            side.Controls.Add(hint);
            ResumeLayout();
            ShowPage(page);
            content.Resize += delegate { if (!rebuilding) { rebuilding = true; ShowPage(page); rebuilding = false; } };
            if (IsHandleCreated) { Native.SetDarkTitleBar(Handle, P.Dark); Refresh(); }
        }
        bool rebuilding;
        int lastWidth;

        public void ShowPage(int i)
        {
            if (content == null) return;
            int w = content.ClientSize.Width;
            if (i == page && w == lastWidth && content.Controls.Count > 0 && !forceRebuild) return;
            forceRebuild = false;
            lastWidth = w;
            page = i;
            for (int n = 0; n < navs.Count; n++) { navs[n].Selected = n == i; navs[n].Invalidate(); }
            content.SuspendLayout();
            int scroll = content.VerticalScroll.Value;
            foreach (Control c in content.Controls) c.Dispose();
            content.Controls.Clear();
            content.AutoScrollPosition = Point.Empty;
            perfTimer.Stop(); perfRam = null;
            y = (int)(28 * k);
            switch (i)
            {
                case 0: PageGeneral(); break;
                case 1: PageAppearance(); break;
                case 2: PageWidgets(); break;
                case 3: PageOrganizer(); break;
                case 4: PagePerformance(); break;
                default: PageAbout(); break;
            }
            EndCard();
            content.Controls.Add(new Label { Location = new Point(0, y), Size = new Size(1, (int)(24 * k)) }); // bottom spacing
            content.ResumeLayout();
            if (keepScroll) { content.AutoScrollPosition = new Point(0, scroll); keepScroll = false; }
        }
        bool forceRebuild, keepScroll;
        void Refresh(int keep) { forceRebuild = true; keepScroll = true; ShowPage(page); }
        /// <summary>Re-reads the current page (after fences are added or removed elsewhere).</summary>
        public void RefreshPage() { if (IsHandleCreated) BeginInvoke(new Action(delegate { Refresh(0); })); }

        // ------------------------------------------------------------ page builders
        int y; Card card; int rowY;
        int CardWidth { get { return Math.Max((int)(380 * k), content.ClientSize.Width - (int)(56 * k) - SystemInformation.VerticalScrollBarWidth); } }

        void H1(string title, string sub)
        {
            content.Controls.Add(new Label { UseMnemonic = false, Text = title, Font = fH1, ForeColor = P.Text, AutoSize = true, Location = new Point((int)(26 * k), y) });
            y += (int)(42 * k);
            content.Controls.Add(new Label { UseMnemonic = false, Text = sub, Font = fBody, ForeColor = P.Sub, AutoSize = true, Location = new Point((int)(28 * k), y) });
            y += (int)(34 * k);
        }

        void Section(string title)
        {
            EndCard();
            y += (int)(12 * k);
            content.Controls.Add(new Label { UseMnemonic = false, Text = title, Font = fSection, ForeColor = P.Text, AutoSize = true, Location = new Point((int)(28 * k), y) });
            y += (int)(28 * k);
            card = new Card(P, k) { Location = new Point((int)(28 * k), y), Width = CardWidth, Height = 10 };
            content.Controls.Add(card);
            rowY = 0;
        }

        void EndCard()
        {
            if (card == null) return;
            if (card.Dividers.Count > 0) card.Dividers.RemoveAt(card.Dividers.Count - 1);
            card.Height = Math.Max(rowY, 10);
            y = card.Bottom + (int)(8 * k);
            card = null;
        }

        Label Row(string title, string sub, Control right)
        {
            int h = (int)((string.IsNullOrEmpty(sub) ? 56 : 68) * k);
            int pad = (int)(18 * k);
            int rw = right == null ? 0 : right.Width;
            var t = new Label { UseMnemonic = false, Text = title, Font = fBody, ForeColor = P.Text, BackColor = P.Card, AutoSize = false, AutoEllipsis = true,
                Location = new Point(pad, rowY + (string.IsNullOrEmpty(sub) ? (h - (int)(20 * k)) / 2 : (int)(14 * k))), Size = new Size(card.Width - rw - 3 * pad, (int)(20 * k)) };
            card.Controls.Add(t);
            Label s = null;
            if (!string.IsNullOrEmpty(sub))
            {
                s = new Label { UseMnemonic = false, Text = sub, Font = fSmall, ForeColor = P.Sub, BackColor = P.Card, AutoSize = false, AutoEllipsis = true,
                    Location = new Point(pad, rowY + (int)(36 * k)), Size = new Size(card.Width - rw - 3 * pad, (int)(20 * k)) };
                card.Controls.Add(s);
            }
            if (right != null)
            {
                right.BackColor = P.Card;
                right.Location = new Point(card.Width - pad - rw, rowY + (h - right.Height) / 2);
                card.Controls.Add(right);
            }
            rowY += h;
            card.Dividers.Add(rowY);
            return s;
        }

        ToggleSwitch Toggle(bool value, Action<bool> set)
        {
            var t = new ToggleSwitch(P, k);
            t.SetSilently(value);
            t.CheckedChanged += delegate { set(t.Checked); S.Save(); app.ApplySettings(); };
            return t;
        }

        FlatBtn Button(string text, bool primary, EventHandler click)
        {
            var b = new FlatBtn(P, k, text, primary) { Font = fBody };
            using (var g = CreateGraphics()) b.Width = (int)(g.MeasureString(text, fBody).Width + 34 * k);
            b.Click += click;
            return b;
        }

        // ------------------------------------------------------------ pages
        void PageGeneral()
        {
            H1("General", "How GlassDesk starts and behaves.");
            Section("Startup");
            Row("Start with Windows", "Launch GlassDesk automatically when you sign in", Toggle(S.StartWithWindows, v => S.StartWithWindows = v));
            Row("Show / hide shortcut", "Press Ctrl + Alt + G to show or hide every panel", Toggle(S.Hotkey, v => S.Hotkey = v));
            Section("Behavior");
            Row("Lock positions", "Stop panels from being moved or resized by accident", Toggle(S.LockPositions, v => S.LockPositions = v));
            Row("Keep behind windows", "Panels stay on the desktop, underneath your apps", Toggle(S.KeepBehindWindows, v => S.KeepBehindWindows = v));
            Row("Open with a single click", "Open fence items with one click instead of a double-click", Toggle(S.SingleClickOpen, v => S.SingleClickOpen = v));
        }

        void PageAppearance()
        {
            H1("Appearance", "Glass, colors and size of everything on your desktop.");
            Section("Theme");
            string[] modes = { "Auto", "Dark", "Light" };
            var seg = new Segmented(P, k, modes, Math.Max(0, Array.IndexOf(modes, S.Theme))) { Font = fSmall };
            seg.SelectionChanged += delegate { S.Theme = modes[seg.SelectedIndex]; S.Save(); app.ApplySettings(); };
            Row("Mode", "Auto follows Windows", seg);

            Color[] colors = { Color.FromArgb(124, 156, 255), Color.FromArgb(0, 120, 212), Color.FromArgb(138, 99, 255), Color.FromArgb(236, 72, 153),
                               Color.FromArgb(255, 122, 69), Color.FromArgb(234, 170, 30), Color.FromArgb(34, 190, 130), Color.FromArgb(20, 176, 196) };
            var sw = new FlowLayoutPanel { AutoSize = false, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            var all = new List<Swatch>();
            bool matched = false;
            foreach (Color c in colors)
            {
                var s = new Swatch(P, k, c) { Margin = new Padding(0), BackColor = P.Card, Selected = c.ToArgb() == S.Accent.ToArgb() };
                matched |= s.Selected;
                all.Add(s); sw.Controls.Add(s);
                Color cc = c;
                s.Click += delegate { S.Accent = cc; S.Save(); app.ApplySettings(); };
            }
            var custom = new Swatch(P, k, S.Accent) { IsCustom = true, Margin = new Padding(0), BackColor = P.Card, Selected = !matched, Font = fBody };
            custom.Click += delegate
            {
                using (var cd = new ColorDialog { Color = S.Accent, FullOpen = true })
                    if (cd.ShowDialog(this) == DialogResult.OK) { S.Accent = cd.Color; S.Save(); app.ApplySettings(); }
            };
            sw.Controls.Add(custom);
            sw.Size = new Size(custom.Width * (colors.Length + 1), custom.Height);
            Row("Accent color", "Highlights and bars", sw);

            Section("Glass");
            Row("Blur behind panels", "Frosted-glass look (Windows 10 and 11)", Toggle(S.Blur, v => S.Blur = v));
            SliderRow("Glass opacity", "{0}%  ·  lower is more see-through", 20, 95, 5, S.Opacity, v => S.Opacity = v);
            SliderRow("Corner radius", "{0} px", 0, 28, 2, S.Radius, v => S.Radius = v);
            SliderRow("Panel size", "{0}%  ·  scales text, icons and widgets", 80, 150, 5, S.UiScale, v => S.UiScale = v);
        }

        void SliderRow(string title, string fmt, int min, int max, int step, int value, Action<int> set)
        {
            var sl = new Slider(P, k, min, max, value) { Step = step };
            Label sub = Row(title, string.Format(fmt, value), sl);
            var t = new Timer { Interval = 120 };   // apply after the user pauses dragging (keeps CPU low)
            t.Tick += delegate { t.Stop(); S.Save(); app.ApplySettings(); };
            sl.ValueChanged += delegate { set(sl.Value); sub.Text = string.Format(fmt, sl.Value); t.Stop(); t.Start(); };
            sl.Disposed += delegate { t.Dispose(); };
        }

        void PageWidgets()
        {
            H1("Widgets", "Small glass widgets that live on your desktop.");
            Section("Clock");
            Row("Clock", "Time, date and a friendly greeting", Toggle(S.ShowClock, v => S.ShowClock = v));
            Row("Show seconds", "Updates every second (otherwise once a minute)", Toggle(S.ShowSeconds, v => S.ShowSeconds = v));
            Row("24-hour time", "Turn off for 12-hour time with AM / PM", Toggle(S.Clock24, v => S.Clock24 = v));
            Section("More widgets");
            Row("System monitor", "CPU, memory, disk and battery", Toggle(S.ShowSystem, v => S.ShowSystem = v));
            Row("Calendar", "Month view — click the arrows to change month", Toggle(S.ShowCalendar, v => S.ShowCalendar = v));
            Row("Notes", "A sticky note — double-click it to write", Toggle(S.ShowNotes, v => S.ShowNotes = v));
            Section("Layout");
            Row("Reset widget positions", "Move all widgets back to the right side of the screen",
                Button("Reset", false, delegate { app.ResetLayout(false); }));
        }

        void PageOrganizer()
        {
            H1("Organizer", "Your desktop files, sorted into glass fences automatically.");
            Section("Desktop fences");
            Row("Show desktop fences", "Group desktop items by type", Toggle(S.ShowFences, v => S.ShowFences = v));
            Row("Hide empty fences", "Only show a fence when it has something in it", Toggle(S.HideEmptyFences, v => S.HideEmptyFences = v));
            Row("Hide Windows desktop icons", "Recommended: the fences replace the normal icons, so nothing overlaps", Toggle(S.HideDesktopIcons, v => S.HideDesktopIcons = v));

            Section("Your fences");
            foreach (string id in S.CustomFences.ToArray())
            {
                string fid = id, name;
                if (!S.Titles.TryGetValue(fid, out name)) name = "My fence";
                int count = 0;
                foreach (var kv in S.Pins) if (kv.Value == fid) count++;
                var both = new FlowLayoutPanel { WrapContents = false, AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = P.Card };
                var rn = Button("Rename", false, delegate
                {
                    string nn = InputBox.Ask("Rename fence", name, app.Theme);
                    if (nn == null || nn.Trim().Length == 0) return;
                    S.Titles[fid] = nn.Trim(); S.Save(); app.Rebuild(); Refresh(0);
                });
                var del = Button("Delete", false, delegate { app.DeleteCustomFence(fid); });
                rn.Margin = new Padding(0, 0, (int)(8 * k), 0); del.Margin = Padding.Empty;
                both.Controls.Add(rn); both.Controls.Add(del);
                both.Size = new Size(rn.Width + del.Width + (int)(8 * k), rn.Height);
                both.AutoSize = false;
                Row(name, count == 1 ? "1 item" : count + " items", both);
            }
            Row("New fence", "Make your own fence, then drag icons into it", Button("New fence…", true, delegate { app.NewFence(); }));

            Section("Drag & drop");
            Row("Between fences", "Drag an icon to another fence to keep it there", null);
            Row("Into a folder", "Drop an icon on a folder to move it inside (hold Ctrl to copy)", null);
            Row("From / to File Explorer", "Drag files in from Explorer, or drag icons out to Explorer and other apps", null);
            Row("Recycle Bin", "Drop an icon on the Recycle Bin to delete it", null);

            Section("Categories");
            foreach (var c in Organizer.Categories)
            {
                string key = c[0];
                string sub = c[2].Length > 0 ? c[2].Replace(" ", "  ") : (key == "folders" ? "Folders on your desktop" : "Everything that doesn't fit elsewhere");
                Row(c[1], sub, Toggle(S.CategoryOn(key), v => S.Categories[key] = v));
            }

            Section("Folder fences");
            foreach (string f in S.FolderFences.ToArray())
            {
                string path = f;
                Row(System.IO.Path.GetFileName(path.TrimEnd('\\')) , path, Button("Remove", false, delegate
                {
                    S.FolderFences.Remove(path); S.Save(); app.Rebuild(); Refresh(0);
                }));
            }
            Row("Add a folder", "Show any folder (Downloads, a project…) as its own fence", Button("Add folder…", true, delegate
            {
                using (var fb = new FolderBrowserDialog { Description = "Choose a folder to show as a fence" })
                    if (fb.ShowDialog(this) == DialogResult.OK && !S.FolderFences.Contains(fb.SelectedPath))
                    {
                        S.FolderFences.Add(fb.SelectedPath); S.Save(); app.Rebuild(); Refresh(0);
                    }
            }));

            Section("Layout");
            Row("Rescan desktop", "GlassDesk watches for changes, but you can refresh now", Button("Rescan", false, delegate { app.Rebuild(); }));
            Row("Reset fence layout", "Put all fences back in a tidy column", Button("Reset", false, delegate { app.ResetLayout(true); }));
        }

        void PagePerformance()
        {
            H1("Performance", "GlassDesk is built to stay out of the way.");
            Section("Right now");
            card.Height = 10;
            int cw = card.Width / 3;
            perfRam = BigStat(0, cw, "Memory");
            perfCpu = BigStat(cw, cw, "CPU");
            perfInfo = BigStat(2 * cw, cw, "Panels");
            rowY = (int)(108 * k);
            card.Dividers.Add(rowY);
            Section("Save power");
            Row("Low power mode", "Refresh the system monitor every 5 seconds instead of 2", Toggle(S.LowPower, v => S.LowPower = v));
            Row("Pause during full-screen apps", "No updates at all while you play games or watch videos", Toggle(S.PauseFullscreen, v => S.PauseFullscreen = v));
            Row("Trim memory when idle", "Give unused memory back to Windows every few minutes", Toggle(S.TrimMemory, v => S.TrimMemory = v));
            Section("How it stays light");
            Row("No animation loops", "Panels are redrawn only when something actually changes", null);
            Row("Event-based organizer", "Windows tells GlassDesk when desktop files change — no polling", null);
            Row("Native code", "A single small .exe using the built-in .NET Framework — no browser engine", null);
            lastCpu = Process.GetCurrentProcess().TotalProcessorTime; lastAt = DateTime.Now;
            UpdatePerf();
            perfTimer.Start();
        }

        Label BigStat(int x, int w, string caption)
        {
            var v = new Label { Text = "…", Font = fBig, ForeColor = P.Text, BackColor = P.Card, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x, (int)(18 * k)), Size = new Size(w, (int)(46 * k)) };
            var c = new Label { Text = caption, Font = fSmall, ForeColor = P.Sub, BackColor = P.Card, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x, (int)(66 * k)), Size = new Size(w, (int)(22 * k)) };
            card.Controls.Add(v); card.Controls.Add(c);
            return v;
        }

        void UpdatePerf()
        {
            if (perfRam == null) return;
            try
            {
                var p = Process.GetCurrentProcess();
                p.Refresh();
                perfRam.Text = (p.WorkingSet64 / 1048576.0).ToString("0") + " MB";
                TimeSpan cpu = p.TotalProcessorTime; DateTime now = DateTime.Now;
                double ms = (now - lastAt).TotalMilliseconds;
                double pct = ms > 0 ? (cpu - lastCpu).TotalMilliseconds / ms / Environment.ProcessorCount * 100 : 0;
                lastCpu = cpu; lastAt = now;
                perfCpu.Text = pct.ToString(pct < 1 ? "0.0" : "0") + "%";
                perfInfo.Text = app.VisibleCount.ToString();
            }
            catch { }
        }

        void PageAbout()
        {
            H1("About", "GlassDesk — a light, glassy desktop organizer for Windows.");
            Section("Shortcuts & tips");
            Row("Ctrl + Alt + G", "Show or hide all panels", null);
            Row("Drag a title bar", "Move a fence. Drag the bottom-right corner to resize it", null);
            Row("Double-click a fence title", "Roll the fence up / down", null);
            Row("Right-click", "Menus for items, fences and widgets", null);
            Section("App");
            Row("Settings file", Settings.Folder, Button("Open folder", false, delegate
            {
                try { System.IO.Directory.CreateDirectory(Settings.Folder); Process.Start("explorer.exe", "\"" + Settings.Folder + "\""); } catch { }
            }));
            var exit = Button("Exit", false, delegate { app.Exit(); });
            exit.Danger = true;
            Row("Exit GlassDesk", "Closes all panels and the tray icon", exit);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            S.Save();
        }

        class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel() { DoubleBuffered = true; }
        }

        class LogoBox : Control
        {
            readonly App app;
            public LogoBox(App app, float k) { this.app = app; Size = new Size((int)(32 * k), (int)(32 * k)); DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                App.DrawLogo(e.Graphics, new RectangleF(0, 0, Width, Height), app.Theme.Accent);
            }
        }
    }
}
