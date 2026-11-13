// GlassDesk 1.8 - the search launcher (Ctrl + Alt + F) and the welcome screen.
// Both are drawn in the theme the user picked: the blurred wallpaper, the theme's glass colors and glow.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GlassDesk
{
    /// <summary>A borderless window painted like a GlassDesk panel.</summary>
    internal class GlassForm : Form
    {
        protected readonly App app; protected readonly Theme T; protected readonly Palette P; protected readonly float k;
        protected int Radius;

        public GlassForm(App app)
        {
            this.app = app; T = app.Theme; P = Palette.From(T); k = app.DpiScale;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false; KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = P.Bg;
            Radius = (int)(Math.Max(8, Math.Min(18, app.Settings.Radius)) * k);
            Text = "GlassDesk";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Native.IsWindows && Native.WinBuild >= 22000) Native.SetRoundCorners(Handle);
            else UpdateRegion();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (!(Native.IsWindows && Native.WinBuild >= 22000)) UpdateRegion(); }
        void UpdateRegion()
        {
            if (Width < 4 || Height < 4) return;
            using (var p = Gfx.Round(new RectangleF(0, 0, Width, Height), Radius)) { var old = Region; Region = new Region(p); if (old != null) old.Dispose(); }
        }

        /// <summary>Blurred wallpaper, the theme's tint, its glow and a soft light from the top.</summary>
        protected void PaintGlass(Graphics g)
        {
            var r = new Rectangle(0, 0, Width, Height);
            using (var b = new SolidBrush(P.Bg)) g.FillRectangle(b, r);
            if (!T.Solid)
                try
                {
                    Rectangle scr = app.Preview ? new Rectangle(0, 0, 1920, 1080) : Screen.FromControl(this).Bounds;
                    Bitmap blur = Backdrop.For(scr, 2);
                    if (blur != null)
                    {
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        g.DrawImage(blur, r, new Rectangle(Left - scr.X, Top - scr.Y, Width, Height), GraphicsUnit.Pixel);
                    }
                }
                catch (Exception ex) { Program.Log(ex); }
            // a stronger tint than desktop panels so text stays easy to read
            int a = T.Solid ? 255 : Math.Max(T.Dark ? 165 : 150, (int)T.TintTop.A);
            using (var b = new LinearGradientBrush(new Point(0, -1), new Point(0, Height + 1), Color.FromArgb(a, T.TintTop), Color.FromArgb(a, T.TintBottom))) g.FillRectangle(b, r);
            if (!T.Glow.IsEmpty) Gfx.Glow(g, new PointF(Width * 0.92f, -Height * 0.05f), Math.Max(Width, Height) * 0.6f, T.Glow);
            if (!T.Glow2.IsEmpty) Gfx.Glow(g, new PointF(Width * 0.05f, Height * 1.05f), Math.Max(Width, Height) * 0.45f, T.Glow2);
            if (!T.Flat)
                using (var b = new LinearGradientBrush(new Point(0, 0), new Point(0, (int)(Height * 0.45f) + 1), Color.FromArgb(T.Dark ? 16 : 40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
                    g.FillRectangle(b, 0, 0, Width, (int)(Height * 0.45f));
            Color rim = T.Neon ? Gfx.WithAlpha(T.Accent, 200) : (T.Dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0));
            using (var p = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius)) using (var pen = new Pen(rim, T.Neon ? 1.6f * k : 1f)) g.DrawPath(pen, p);
        }

        /// <summary>A soft rounded "chip": the search field, buttons and tiles.</summary>
        protected void Chip(Graphics g, RectangleF r, float radius, bool hot)
        {
            Color fill = T.Dark ? Color.FromArgb(hot ? 34 : 20, 255, 255, 255) : Color.FromArgb(hot ? 150 : 110, 255, 255, 255);
            Color line = T.Dark ? Color.FromArgb(hot ? 50 : 28, 255, 255, 255) : Color.FromArgb(hot ? 34 : 20, 0, 0, 0);
            using (var p = Gfx.Round(r, radius)) { using (var b = new SolidBrush(fill)) g.FillPath(b, p); using (var pen = new Pen(line, 1f)) g.DrawPath(pen, p); }
        }

        protected void AccentButton(Graphics g, RectangleF r, string text, Font f, bool hot)
        {
            using (var p = Gfx.Round(r, r.Height / 2))
            {
                using (var b = new LinearGradientBrush(r, P.Accent, P.Accent2, 0f)) g.FillPath(b, p);
                if (hot) using (var b = new SolidBrush(Color.FromArgb(36, 255, 255, 255))) g.FillPath(b, p);
            }
            using (var b = new SolidBrush(Color.White)) g.DrawString(text, f, b, r, Gfx.Center);
        }

        protected void Close(Graphics g, RectangleF r, bool hot)
        {
            if (hot) Chip(g, r, r.Height / 2, true);
            float s = r.Width * 0.18f, cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var pen = new Pen(hot ? P.Text : P.Sub, 1.6f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            { g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s); g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s); }
        }

        protected override void OnPaint(PaintEventArgs e) { Gfx.Setup(e.Graphics); PaintAll(e.Graphics); }
        protected virtual void PaintAll(Graphics g) { PaintGlass(g); }

        /// <summary>The window as a picture (used for screenshots).</summary>
        public Bitmap Render()
        {
            var bmp = new Bitmap(Width, Height);
            using (var g = Graphics.FromImage(bmp)) { Gfx.Setup(g); PaintAll(g); }
            return bmp;
        }
    }

    // =================================================================== search launcher
    internal class LauncherForm : GlassForm
    {
        readonly List<KeyValuePair<DesktopItem, string>> all;
        readonly List<DesktopItem> shown = new List<DesktopItem>();
        string query = "";
        int sel, hot = -1, scroll;
        bool caretOn = true, closing, hotClose, hotSettings;
        readonly Timer caret;
        const int Cols = 7;

        public LauncherForm(App app, List<KeyValuePair<DesktopItem, string>> items) : base(app)
        {
            all = items;
            TopMost = true;
            int w = (int)(700 * k), h = (int)(500 * k);
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + Math.Max(0, (wa.Height - h) / 3), w, h);
            caret = new Timer { Interval = 530 };
            caret.Tick += delegate { caretOn = !caretOn; Invalidate(SearchRect); };
            Filter();
            Load += delegate { caret.Start(); };
            Deactivate += delegate { Finish(); };
            FormClosed += delegate { caret.Dispose(); };
        }

        // ---------- layout ----------
        int Pad { get { return (int)(16 * k); } }
        Rectangle SearchRect { get { return new Rectangle(Pad, Pad, Width - 2 * Pad - (int)(48 * k), (int)(46 * k)); } }
        Rectangle CloseRect { get { var s = SearchRect; return new Rectangle(s.Right + (int)(10 * k), s.Y + (s.Height - (int)(36 * k)) / 2, (int)(36 * k), (int)(36 * k)); } }
        // ---------- quick actions: a calculator, themes, commands and web search ----------
        class Act { public string Glyph, Title, Hint; public Action Run; }
        readonly List<Act> acts = new List<Act>();
        int ActH { get { return (int)(46 * k); } }
        Rectangle ActRect(int i) { return new Rectangle(Pad, SearchRect.Bottom + (int)(12 * k) + i * (ActH + (int)(6 * k)), Width - 2 * Pad, ActH); }
        int hotAct = -1;

        void BuildActs()
        {
            acts.Clear();
            string q = query.Trim();
            if (q.Length == 0) return;
            double v;
            if (Calc.TryEval(q, out v))
            {
                string res = Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-6 && v != 0) ? v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) : Math.Round(v, 8).ToString("#,0.########", System.Globalization.CultureInfo.InvariantCulture);
                acts.Add(new Act { Glyph = "=", Title = "= " + res, Hint = "Enter to copy", Run = delegate { try { Clipboard.SetText(res.Replace(",", "")); } catch { } Finish(); } });
            }
            string ql = q.ToLowerInvariant();
            foreach (var list in new[] { Themes.All, Themes.Windows })
                foreach (var l in list)
                    if (acts.Count < 3 && ql.Length >= 3 && (l.Name.ToLowerInvariant().StartsWith(ql) || ("theme " + l.Name.ToLowerInvariant()).StartsWith(ql)))
                    {
                        var look = l;
                        acts.Add(new Act { Glyph = "◐", Title = "Switch to the " + l.Name + " theme", Hint = l.Desc, Run = delegate { Finish(); Themes.Apply(app.Settings, look); app.Settings.Save(); app.ApplySettings(); } });
                    }
            string[][] cmds =
            {
                new[] { "settings", "Open Settings" }, new[] { "organize", "Organize desktop" }, new[] { "hide", "Hide or show all panels" },
                new[] { "new fence", "Make a new fence" }, new[] { "dark", "Dark mode" }, new[] { "light", "Light mode" },
            };
            foreach (var c in cmds)
                if (acts.Count < 3 && ql.Length >= 3 && c[0].StartsWith(ql))
                {
                    string id = c[0];
                    acts.Add(new Act { Glyph = "›", Title = c[1], Hint = "GlassDesk command", Run = delegate
                    {
                        Finish();
                        if (id == "settings") app.ShowSettings();
                        else if (id == "organize") app.ShowOrganize();
                        else if (id == "hide") app.ToggleVisible();
                        else if (id == "new fence") app.NewFence();
                        else { app.Settings.Theme = id == "dark" ? "Dark" : "Light"; app.Settings.Save(); app.ApplySettings(); }
                    } });
                }
            if (acts.Count < 3)
                acts.Add(new Act { Glyph = "⌕", Title = "Search the web for \"" + q + "\"", Hint = shown.Count == 0 ? "Enter" : "Ctrl + Enter", Run = delegate
                {
                    Finish();
                    try { Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(q)) { UseShellExecute = true }); } catch { }
                } });
        }

        void RunFirstAct() { if (acts.Count > 0) acts[0].Run(); }

        Rectangle GridRect { get { int top = SearchRect.Bottom + (int)(14 * k) + acts.Count * (ActH + (int)(6 * k)); return new Rectangle(Pad, top, Width - 2 * Pad, FooterTop - top - (int)(4 * k)); } }
        int FooterTop { get { return Height - (int)(54 * k); } }
        Rectangle SettingsRect { get { int bw = (int)(140 * k), bh = (int)(34 * k); return new Rectangle(Width - Pad - bw, FooterTop + ((Height - FooterTop) - bh) / 2, bw, bh); } }
        int CellW { get { return (GridRect.Width - (int)(10 * k)) / Cols; } }
        int CellH { get { return (int)(96 * k); } }
        int Rows { get { return (shown.Count + Cols - 1) / Cols; } }
        int MaxScroll { get { return Math.Max(0, Rows * CellH - GridRect.Height); } }
        Rectangle Cell(int i)
        {
            var gr = GridRect;
            return new Rectangle(gr.X + (i % Cols) * CellW, gr.Y + (i / Cols) * CellH - scroll, CellW, CellH);
        }

        // ---------- data ----------
        void Filter()
        {
            string q = query.Trim();
            var starts = new List<DesktopItem>(); var has = new List<DesktopItem>(); var loose = new List<DesktopItem>();
            foreach (var kv in all)
            {
                string n = kv.Key.Name;
                if (q.Length == 0 || n.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) starts.Add(kv.Key);
                else if (n.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) has.Add(kv.Key);
                else if (Loose(q, n)) loose.Add(kv.Key);
            }
            shown.Clear(); shown.AddRange(starts); shown.AddRange(has); shown.AddRange(loose);
            BuildActs();
            sel = 0; hot = -1; scroll = 0;
            Invalidate();
        }

        /// <summary>"bdg" finds "Budget 2026" - letters in order, gaps allowed.</summary>
        static bool Loose(string q, string name)
        {
            int j = 0; q = q.ToLowerInvariant(); name = name.ToLowerInvariant();
            foreach (char c in name) if (j < q.Length && c == q[j]) j++;
            return q.Length >= 2 && j == q.Length;
        }

        public void SetQuery(string q) { query = q; Filter(); }

        // ---------- painting ----------
        protected override void PaintAll(Graphics g)
        {
            PaintGlass(g);
            var fBig = Gfx.F(T.FontName, 16 * k, FontStyle.Regular);
            var fName = Gfx.F(T.FontName, 12 * k, FontStyle.Regular);
            var fSmall = Gfx.F(T.FontName, 11.5f * k, FontStyle.Regular);

            // search field
            var s = SearchRect;
            Chip(g, s, s.Height / 2f, true);
            float ic = 18 * k, ix = s.X + 18 * k, iy = s.Y + (s.Height - ic) / 2;
            using (var pen = new Pen(P.Accent, 2.2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(pen, ix, iy, ic * 0.72f, ic * 0.72f);
                g.DrawLine(pen, ix + ic * 0.64f, iy + ic * 0.64f, ix + ic * 0.95f, iy + ic * 0.95f);
            }
            float tx = ix + ic + 14 * k;
            string kbd = "Ctrl + Alt + F";
            SizeF ks = g.MeasureString(kbd, fSmall);
            var kr = new RectangleF(s.Right - ks.Width - 30 * k, s.Y + (s.Height - 24 * k) / 2, ks.Width + 16 * k, 24 * k);
            Chip(g, kr, 7 * k, false);
            using (var b = new SolidBrush(P.Text)) g.DrawString(kbd, fSmall, b, kr, Gfx.Center);
            var textR = new RectangleF(tx, s.Y, kr.X - tx - 10 * k, s.Height);
            if (query.Length == 0)
                using (var b = new SolidBrush(Gfx.WithAlpha(P.Sub, 200))) g.DrawString("Search your desktop…", fBig, b, textR, Gfx.Left);
            else
                using (var b = new SolidBrush(P.Text)) g.DrawString(query, fBig, b, textR, Gfx.Left);
            if (caretOn)
            {
                float cw = query.Length == 0 ? 0 : g.MeasureString(query, fBig, PointF.Empty, StringFormat.GenericTypographic).Width;
                float cx = Math.Min(textR.Right, tx + cw + 2 * k);
                using (var pen = new Pen(P.Accent, 1.8f * k)) g.DrawLine(pen, cx, s.Y + s.Height * 0.28f, cx, s.Y + s.Height * 0.72f);
            }
            Close(g, CloseRect, hotClose);

            // quick actions
            for (int i = 0; i < acts.Count; i++)
            {
                var ar = ActRect(i);
                bool firstAct = i == 0 && (shown.Count == 0 || acts[0].Glyph == "=");
                if (firstAct)
                    using (var p = Gfx.Round(ar, 12 * k))
                    {
                        using (var b = new LinearGradientBrush(ar, Gfx.WithAlpha(P.Accent, T.Dark ? 70 : 55), Gfx.WithAlpha(P.Accent2, T.Dark ? 40 : 30), 0f)) g.FillPath(b, p);
                        using (var pen = new Pen(Gfx.WithAlpha(P.Accent, 150), 1.2f * k)) g.DrawPath(pen, p);
                    }
                else Chip(g, ar, 12 * k, i == hotAct);
                var gr0 = new RectangleF(ar.X + 10 * k, ar.Y + (ar.Height - 28 * k) / 2, 28 * k, 28 * k);
                using (var p = Gfx.Round(gr0, 8 * k)) using (var b = new LinearGradientBrush(gr0, P.Accent, P.Accent2, 45f)) g.FillPath(b, p);
                using (var b = new SolidBrush(Color.White)) g.DrawString(acts[i].Glyph, Gfx.F(T.FontName, 19 * k, FontStyle.Bold), b, gr0, Gfx.Center);
                var fT = Gfx.F(T.FontName, acts[i].Glyph == "=" ? 17 * k : 14 * k, FontStyle.Bold);
                float hx = ar.Right - 230 * k;
                using (var b = new SolidBrush(P.Text)) g.DrawString(acts[i].Title, fT, b, new RectangleF(gr0.Right + 12 * k, ar.Y, hx - gr0.Right - 16 * k, ar.Height), Gfx.Left);
                using (var b = new SolidBrush(P.Sub)) g.DrawString(acts[i].Hint, fSmall, b, new RectangleF(hx, ar.Y, 218 * k, ar.Height), Gfx.Right);
            }

            // grid of icons
            var gr = GridRect;
            var st = g.Save();
            g.SetClip(gr);
            if (shown.Count == 0 && acts.Count == 0)
                using (var b = new SolidBrush(P.Sub)) g.DrawString(query.Length == 0 ? "Your desktop is empty" : "Nothing on your desktop matches \"" + query + "\"", fBig, b, gr, Gfx.Center);
            int first = Math.Max(0, scroll / CellH * Cols), last = Math.Min(shown.Count, ((scroll + gr.Height) / CellH + 1) * Cols);
            for (int i = first; i < last; i++)
            {
                var c = Cell(i);
                var box = new RectangleF(c.X + 3 * k, c.Y + 3 * k, c.Width - 6 * k, c.Height - 6 * k);
                if (i == sel)
                    using (var p = Gfx.Round(box, 12 * k))
                    {
                        using (var b = new LinearGradientBrush(box, Gfx.WithAlpha(P.Accent, T.Dark ? 70 : 55), Gfx.WithAlpha(P.Accent2, T.Dark ? 40 : 30), 45f)) g.FillPath(b, p);
                        using (var pen = new Pen(Gfx.WithAlpha(P.Accent, 150), 1.2f * k)) g.DrawPath(pen, p);
                    }
                else if (i == hot) Chip(g, box, 12 * k, false);
                var it = shown[i];
                int isz = (int)(44 * k);
                var ir = new Rectangle(c.X + (c.Width - isz) / 2, c.Y + (int)(12 * k), isz, isz);
                var icon = it.Icon;
                if (icon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(icon, ir); }
                else
                {
                    // no icon (only in screenshots): a tile with the first letter
                    using (var p = Gfx.Round(ir, 10 * k)) using (var b = new LinearGradientBrush(ir, P.Accent, P.Accent2, 45f)) g.FillPath(b, p);
                    using (var b = new SolidBrush(Color.White)) g.DrawString(it.Name.Substring(0, Math.Min(1, it.Name.Length)).ToUpperInvariant(), Gfx.F(T.FontName, 18 * k, FontStyle.Bold), b, ir, Gfx.Center);
                }
                using (var b = new SolidBrush(P.Text))
                    g.DrawString(it.Name, fName, b, new RectangleF(c.X + 6 * k, ir.Bottom + 6 * k, c.Width - 12 * k, 20 * k), new StringFormat(Gfx.Center) { Trimming = StringTrimming.EllipsisCharacter });
            }
            g.Restore(st);
            // scroll bar
            if (MaxScroll > 0)
            {
                float trackH = gr.Height, thumbH = Math.Max(30 * k, trackH * gr.Height / (Rows * CellH));
                float y = gr.Y + (trackH - thumbH) * scroll / MaxScroll;
                Gfx.FillRound(g, Gfx.WithAlpha(P.Text, 60), new RectangleF(Width - Pad / 2f - 3 * k, y, 4 * k, thumbH), 2 * k);
            }

            // footer
            using (var pen = new Pen(T.Dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0))) g.DrawLine(pen, Pad, FooterTop, Width - Pad, FooterTop);
            using (var b = new SolidBrush(P.Sub))
                g.DrawString(shown.Count + (shown.Count == 1 ? " item" : " items") + "   ·   Enter to open   ·   Shift + Enter to show in folder", fSmall, b,
                    new RectangleF(Pad + 4 * k, FooterTop, SettingsRect.X - Pad, Height - FooterTop), Gfx.Left);
            var sr = SettingsRect;
            Chip(g, sr, 9 * k, hotSettings);
            DrawGear(g, new PointF(sr.X + 20 * k, sr.Y + sr.Height / 2f), 6.5f * k);
            using (var b = new SolidBrush(P.Text)) g.DrawString("Open Settings", fSmall, b, new RectangleF(sr.X + 32 * k, sr.Y, sr.Width - 36 * k, sr.Height), Gfx.Left);
        }

        void DrawGear(Graphics g, PointF c, float r)
        {
            using (var pen = new Pen(P.Sub, 1.6f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(pen, c.X - r * 0.62f, c.Y - r * 0.62f, r * 1.24f, r * 1.24f);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, c.X + (float)Math.Cos(a) * r * 0.78f, c.Y + (float)Math.Sin(a) * r * 0.78f, c.X + (float)Math.Cos(a) * r * 1.1f, c.Y + (float)Math.Sin(a) * r * 1.1f);
                }
            }
        }

        // ---------- input ----------
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar)) { query += e.KeyChar; caretOn = true; Filter(); e.Handled = true; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int n = shown.Count;
            switch (e.KeyCode)
            {
                case Keys.Escape: if (query.Length > 0) SetQuery(""); else Finish(); break;
                case Keys.Back:
                    if (query.Length > 0)
                    {
                        if (e.Control) { string t = query.TrimEnd(); int i = t.LastIndexOf(' '); query = i < 0 ? "" : t.Substring(0, i + 1); }
                        else query = query.Substring(0, query.Length - 1);
                        Filter();
                    }
                    break;
                case Keys.V: if (e.Control) { try { string t = Clipboard.GetText(); if (!string.IsNullOrEmpty(t)) SetQuery(query + t.Split('\r', '\n')[0]); } catch { } } else return; break;
                case Keys.Right: Move(1); break;
                case Keys.Left: Move(-1); break;
                case Keys.Down: Move(Cols); break;
                case Keys.Up: Move(-Cols); break;
                case Keys.Enter:
                    if (e.Control || (acts.Count > 0 && (shown.Count == 0 || acts[0].Glyph == "="))) RunFirstAct();
                    else Open(sel, e.Shift);
                    break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = e.KeyCode != Keys.V || e.Control;
        }

        void Move(int d)
        {
            if (shown.Count == 0) return;
            sel = Math.Max(0, Math.Min(shown.Count - 1, sel + d));
            var c = Cell(sel); var gr = GridRect;
            if (c.Y < gr.Y) scroll -= gr.Y - c.Y;
            else if (c.Bottom > gr.Bottom) scroll += c.Bottom - gr.Bottom;
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
            Invalidate();
        }

        int HitItem(Point p)
        {
            if (!GridRect.Contains(p)) return -1;
            var gr = GridRect;
            int col = (p.X - gr.X) / CellW, row = (p.Y - gr.Y + scroll) / CellH;
            if (col < 0 || col >= Cols) return -1;
            int i = row * Cols + col;
            return i < shown.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int ha = -1; for (int a = 0; a < acts.Count; a++) if (ActRect(a).Contains(e.Location)) ha = a;
            if (ha != hotAct) { hotAct = ha; Invalidate(); }
            int h = HitItem(e.Location);
            bool hc = CloseRect.Contains(e.Location), hs = SettingsRect.Contains(e.Location);
            if (h != hot || hc != hotClose || hs != hotSettings) { hot = h; hotClose = hc; hotSettings = hs; Invalidate(); }
            Cursor = h >= 0 || hc || hs || ha >= 0 ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; hotClose = hotSettings = false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - e.Delta * CellH / 120));
            OnMouseMove(e); Invalidate();
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (CloseRect.Contains(e.Location)) { Finish(); return; }
            for (int a = 0; a < acts.Count; a++) if (ActRect(a).Contains(e.Location)) { acts[a].Run(); return; }
            if (SettingsRect.Contains(e.Location)) { Finish(); app.ShowSettings(); return; }
            int i = HitItem(e.Location);
            if (i >= 0) Open(i, ModifierKeys == Keys.Shift);
        }

        void Open(int i, bool reveal)
        {
            if (i < 0 || i >= shown.Count) return;
            var it = shown[i];
            try
            {
                if (it.IsRecycleBin) Process.Start("explorer.exe", "shell:RecycleBinFolder");
                else if (reveal) Process.Start("explorer.exe", "/select,\"" + it.Path + "\"");
                else Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("Could not open \"" + it.Name + "\".\n\n" + ex.Message, "GlassDesk"); }
            Finish();
        }

        void Finish() { if (closing) return; closing = true; Close(); }
    }

    // =================================================================== welcome screen
    internal class WelcomeForm : GlassForm
    {
        int hotBtn = -1;   // 0 close, 1 settings, 2 get started
        readonly Timer fade;
        Point dragFrom; bool dragging;
        static readonly string[][] Tips =
        {
            new[] { "Fences", "Your files sorted into glass boxes. Drag a fence's edge to resize it." },
            new[] { "Widgets", "Clock, weather, music, to-dos and more. Right-click one for options." },
            new[] { "Quick search", "Press Ctrl + Alt + F to find and open anything on your desktop." },
            new[] { "Themes", "26 looks, from GlassDesk glass to Mica and Acrylic. Settings → Appearance." },
        };

        public WelcomeForm(App app) : base(app)
        {
            ShowInTaskbar = true; TopMost = true;
            if (app.AppIcon != null) Icon = app.AppIcon;
            int w = (int)(640 * k), h = (int)(500 * k);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
            Opacity = app.Preview ? 1 : 0;
            fade = new Timer { Interval = 15 };
            fade.Tick += delegate { Opacity = Math.Min(1, Opacity + 0.08); if (Opacity >= 1) fade.Stop(); };
            Shown += delegate { fade.Start(); };
            FormClosed += delegate { fade.Dispose(); };
        }

        int Pad { get { return (int)(34 * k); } }
        Rectangle CloseRect { get { return new Rectangle(Width - (int)(50 * k), (int)(14 * k), (int)(36 * k), (int)(36 * k)); } }
        Rectangle StartRect { get { int bw = (int)(150 * k), bh = (int)(42 * k); return new Rectangle(Width - Pad - bw, Height - Pad - bh, bw, bh); } }
        Rectangle SettingsRect { get { var s = StartRect; int bw = (int)(140 * k); return new Rectangle(s.X - bw - (int)(12 * k), s.Y, bw, s.Height); } }

        protected override void PaintAll(Graphics g)
        {
            PaintGlass(g);
            // logo with a glow behind it
            int ls = (int)(64 * k);
            var lr = new Rectangle(Pad, Pad + (int)(6 * k), ls, ls);
            Gfx.Glow(g, new PointF(lr.X + ls / 2f, lr.Y + ls / 2f), ls * 1.3f, Gfx.WithAlpha(P.Accent, 70));
            using (var logo = App.LogoBitmap(256, P.Accent)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(logo, lr); }
            float tx = lr.Right + 18 * k;
            using (var b = new SolidBrush(P.Text))
                g.DrawString("Welcome to GlassDesk", Gfx.F(T.DisplayFont, 28 * k, FontStyle.Bold), b, new RectangleF(tx, lr.Y - 2 * k, Width - tx - 60 * k, 40 * k), Gfx.Left);
            using (var b = new SolidBrush(P.Sub))
                g.DrawString("Your desktop, organized in beautiful glass.", Gfx.F(T.FontName, 14.5f * k, FontStyle.Regular), b, new RectangleF(tx, lr.Y + 38 * k, Width - tx - 40 * k, 24 * k), Gfx.Left);
            Close(g, CloseRect, hotBtn == 0);

            // four tips in a 2 x 2 grid
            float gap = 14 * k, top = lr.Bottom + 30 * k;
            float tw = (Width - 2 * Pad - gap) / 2f, th = (StartRect.Y - 26 * k - top - gap) / 2f;
            var fT = Gfx.F(T.FontName, 15 * k, FontStyle.Bold); var fD = Gfx.F(T.FontName, 12.5f * k, FontStyle.Regular);
            for (int i = 0; i < 4; i++)
            {
                var r = new RectangleF(Pad + (i % 2) * (tw + gap), top + (i / 2) * (th + gap), tw, th);
                Chip(g, r, 14 * k, false);
                float d = 40 * k;
                var ic = new RectangleF(r.X + 16 * k, r.Y + 16 * k, d, d);
                using (var p = Gfx.Round(ic, 11 * k)) using (var b = new LinearGradientBrush(ic, P.Accent, P.Accent2, 45f)) g.FillPath(b, p);
                DrawTipIcon(g, i, ic);
                float x = ic.Right + 14 * k;
                using (var b = new SolidBrush(P.Text)) g.DrawString(Tips[i][0], fT, b, new RectangleF(x, r.Y + 14 * k, r.Right - x - 12 * k, 22 * k), Gfx.Left);
                using (var b = new SolidBrush(P.Sub)) g.DrawString(Tips[i][1], fD, b, new RectangleF(x, r.Y + 38 * k, r.Right - x - 14 * k, r.Height - 44 * k), Gfx.TopLeftWrap);
            }

            // buttons
            var fB = Gfx.F(T.FontName, 13.5f * k, FontStyle.Bold);
            using (var b = new SolidBrush(P.Sub))
                g.DrawString("GlassDesk " + App.Version, Gfx.F(T.FontName, 12 * k, FontStyle.Regular), b, new RectangleF(Pad, StartRect.Y, SettingsRect.X - Pad, StartRect.Height), Gfx.Left);
            var sr = SettingsRect;
            Chip(g, sr, sr.Height / 2f, hotBtn == 1);
            using (var b = new SolidBrush(P.Text)) g.DrawString("Open Settings", fB, b, sr, Gfx.Center);
            AccentButton(g, StartRect, "Get started", fB, hotBtn == 2);
        }

        void DrawTipIcon(Graphics g, int i, RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = r.Width * 0.24f;
            using (var pen = new Pen(Color.White, 2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var b = new SolidBrush(Color.White))
            {
                if (i == 0)        // fences: four little boxes
                    for (int j = 0; j < 4; j++)
                        using (var p = Gfx.Round(new RectangleF(cx - s + (j % 2) * s * 1.1f, cy - s + (j / 2) * s * 1.1f, s * 0.9f, s * 0.9f), 2.5f * k)) g.DrawPath(pen, p);
                else if (i == 1)   // widgets: a clock
                {
                    g.DrawEllipse(pen, cx - s, cy - s, 2 * s, 2 * s);
                    g.DrawLine(pen, cx, cy, cx, cy - s * 0.6f); g.DrawLine(pen, cx, cy, cx + s * 0.45f, cy + s * 0.2f);
                }
                else if (i == 2)   // search: a magnifier
                {
                    g.DrawEllipse(pen, cx - s, cy - s, s * 1.5f, s * 1.5f);
                    g.DrawLine(pen, cx + s * 0.35f, cy + s * 0.35f, cx + s, cy + s);
                }
                else               // themes: three color drops
                {
                    float d = s * 1.05f;
                    g.FillEllipse(b, cx - d, cy - d * 0.9f, d, d);
                    using (var b2 = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillEllipse(b2, cx, cy - d * 0.9f, d, d);
                    using (var b3 = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillEllipse(b3, cx - d / 2, cy - d * 0.05f, d, d);
                }
            }
        }

        int Hit(Point p) { return CloseRect.Contains(p) ? 0 : SettingsRect.Contains(p) ? 1 : StartRect.Contains(p) ? 2 : -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { Location = new Point(Left + e.X - dragFrom.X, Top + e.Y - dragFrom.Y); return; }
            int h = Hit(e.Location);
            if (h != hotBtn) { hotBtn = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Hit(e.Location) < 0) { dragging = true; dragFrom = e.Location; }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Invalidate(); return; }
            if (e.Button != MouseButtons.Left) return;
            int h = Hit(e.Location);
            if (h == 0 || h == 2) Close();
            else if (h == 1) { Close(); app.ShowSettings(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hotBtn != -1) { hotBtn = -1; Invalidate(); } }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close(); base.OnKeyDown(e); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Invalidate(); }   // the blurred wallpaper behind it moves
    }

    /// <summary>A tiny calculator for the search box: + - * / ^ %, brackets and decimals.</summary>
    internal static class Calc
    {
        public static bool TryEval(string text, out double v)
        {
            v = 0;
            string q = text.Replace(" ", "").Replace("×", "*").Replace("÷", "/").Replace(",", ".");
            if (q.Length < 3) return false;
            bool op = false, digit = false;
            foreach (char c in q)
            {
                if (char.IsDigit(c)) digit = true;
                else if ("+-*/^%".IndexOf(c) >= 0) op = true;
                else if (c != '.' && c != '(' && c != ')') return false;
            }
            if (!op || !digit) return false;
            try
            {
                int i = 0;
                v = Sum(q, ref i);
                return i == q.Length && !double.IsNaN(v) && !double.IsInfinity(v);
            }
            catch { return false; }
        }
        static double Sum(string s, ref int i)
        {
            double v = Prod(s, ref i);
            while (i < s.Length && (s[i] == '+' || s[i] == '-')) { char o = s[i++]; double r = Prod(s, ref i); v = o == '+' ? v + r : v - r; }
            return v;
        }
        static double Prod(string s, ref int i)
        {
            double v = Pow(s, ref i);
            while (i < s.Length && (s[i] == '*' || s[i] == '/' || s[i] == '%'))
            {
                char o = s[i++];
                if (o == '%' && (i >= s.Length || "+-*/)".IndexOf(s[i]) >= 0)) { v /= 100; continue; }   // "20%" = 0.2
                double r = Pow(s, ref i); v = o == '*' ? v * r : o == '/' ? v / r : v % r;
            }
            return v;
        }
        static double Pow(string s, ref int i)
        {
            double v = Unary(s, ref i);
            if (i < s.Length && s[i] == '^') { i++; v = Math.Pow(v, Pow(s, ref i)); }
            return v;
        }
        static double Unary(string s, ref int i)
        {
            if (i < s.Length && s[i] == '-') { i++; return -Unary(s, ref i); }
            if (i < s.Length && s[i] == '+') { i++; return Unary(s, ref i); }
            if (i < s.Length && s[i] == '(') { i++; double v = Sum(s, ref i); if (i >= s.Length || s[i] != ')') throw new FormatException(); i++; return v; }
            int st = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (st == i) throw new FormatException();
            return double.Parse(s.Substring(st, i - st), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
