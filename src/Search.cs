// GlassDesk 1.3 - quick search (Ctrl + Alt + F) and the "add rule" dialog.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GlassDesk
{
    internal class SearchForm : Form
    {
        readonly Theme t; readonly float k;
        readonly TextBox box; readonly ListBox list; readonly Label hint;
        readonly List<KeyValuePair<DesktopItem, string>> all;   // item, fence caption
        readonly List<KeyValuePair<DesktopItem, string>> shown = new List<KeyValuePair<DesktopItem, string>>();
        bool closing;

        public SearchForm(App app, List<KeyValuePair<DesktopItem, string>> items)
        {
            t = app.Theme; k = app.DpiScale; all = items;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            Color bg = t.Dark ? Color.FromArgb(26, 28, 36) : Color.FromArgb(250, 250, 252), fg = t.Dark ? Color.FromArgb(240, 242, 248) : Color.FromArgb(20, 22, 28);
            BackColor = bg; ForeColor = fg;
            int w = (int)(580 * k);
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + wa.Height / 5, w, (int)(430 * k));
            Padding = new Padding((int)(14 * k));

            box = new TextBox { Dock = DockStyle.Top, BorderStyle = BorderStyle.None, BackColor = bg, ForeColor = fg, Font = Gfx.SafeFont(t.FontName, 22 * k, FontStyle.Regular) };
            hint = new Label { Dock = DockStyle.Bottom, Height = (int)(26 * k), ForeColor = t.Dark ? Color.FromArgb(140, 148, 165) : Color.Gray, Font = Gfx.SafeFont(t.FontName, 11.5f * k, FontStyle.Regular),
                Text = "Enter to open  ·  Shift + Enter to show in File Explorer  ·  Esc to close", TextAlign = ContentAlignment.MiddleLeft };
            var gap = new Panel { Dock = DockStyle.Top, Height = (int)(10 * k), BackColor = bg };
            list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = bg, ForeColor = fg, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = (int)(44 * k), IntegralHeight = false };
            list.DrawItem += DrawRow;
            list.DoubleClick += delegate { OpenSelected(false); };
            Controls.Add(list); Controls.Add(gap); Controls.Add(box); Controls.Add(hint);

            box.TextChanged += delegate { Filter(); };
            KeyDown += OnKey;
            Deactivate += delegate { Finish(); };
            Load += delegate { if (Native.IsWindows) Native.SetRoundCorners(Handle); Filter(); box.Focus(); };
        }

        void Filter()
        {
            string q = box.Text.Trim();
            shown.Clear();
            var starts = new List<KeyValuePair<DesktopItem, string>>(); var has = new List<KeyValuePair<DesktopItem, string>>(); var loose = new List<KeyValuePair<DesktopItem, string>>();
            foreach (var kv in all)
            {
                string n = kv.Key.Name;
                if (q.Length == 0 || n.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) starts.Add(kv);
                else if (n.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) has.Add(kv);
                else if (Loose(q, n)) loose.Add(kv);
            }
            shown.AddRange(starts); shown.AddRange(has); shown.AddRange(loose);
            list.BeginUpdate(); list.Items.Clear();
            for (int i = 0; i < shown.Count && i < 60; i++) list.Items.Add(shown[i].Key.Name);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.EndUpdate();
            hint.Text = list.Items.Count == 0 ? "Nothing on your desktop matches \"" + q + "\"" : "Enter to open  ·  Shift + Enter to show in File Explorer  ·  Esc to close";
        }

        /// <summary>"bdg" finds "Budget 2026" - letters in order, gaps allowed.</summary>
        static bool Loose(string q, string name)
        {
            int j = 0; q = q.ToLowerInvariant(); name = name.ToLowerInvariant();
            foreach (char c in name) if (j < q.Length && c == q[j]) j++;
            return q.Length >= 2 && j == q.Length;
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, e.Bounds);
            if (sel) Gfx.FillRound(g, Gfx.WithAlpha(t.Accent, t.Dark ? 70 : 50), new RectangleF(e.Bounds.X, e.Bounds.Y + 2, e.Bounds.Width - 2, e.Bounds.Height - 4), 8 * k);
            var it = shown[e.Index].Key;
            int ic = (int)(30 * k);
            var ir = new Rectangle(e.Bounds.X + (int)(8 * k), e.Bounds.Y + (e.Bounds.Height - ic) / 2, ic, ic);
            if (it.Icon != null) g.DrawImage(it.Icon, ir);
            float x = ir.Right + 12 * k;
            using (var b = new SolidBrush(ForeColor))
                g.DrawString(it.Name, Gfx.SafeFont(t.FontName, 14 * k, FontStyle.Regular), b, new RectangleF(x, e.Bounds.Y, e.Bounds.Width - x - 160 * k, e.Bounds.Height), Gfx.Left);
            using (var b = new SolidBrush(t.Dark ? Color.FromArgb(140, 148, 165) : Color.Gray))
                g.DrawString(shown[e.Index].Value, Gfx.SafeFont(t.FontName, 12 * k, FontStyle.Regular), b, new RectangleF(e.Bounds.Right - 160 * k, e.Bounds.Y, 150 * k, e.Bounds.Height), Gfx.Right);
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Finish(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { if (list.SelectedIndex < list.Items.Count - 1) list.SelectedIndex++; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { if (list.SelectedIndex > 0) list.SelectedIndex--; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { OpenSelected(e.Shift); e.Handled = e.SuppressKeyPress = true; }
        }

        void OpenSelected(bool reveal)
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= shown.Count) return;
            var it = shown[i].Key;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Gfx.WithAlpha(t.Accent, 160), 2)) e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }

    /// <summary>"Files whose name matches … go to …"</summary>
    internal class RuleDialog : Form
    {
        public string Pattern, FenceId;
        readonly TextBox pat; readonly ComboBox fence; readonly Label example;
        readonly List<KeyValuePair<string, string>> fences;

        public RuleDialog(App app, List<KeyValuePair<string, string>> fenceChoices, string preselect)
        {
            Theme t = app.Theme; float k = app.DpiScale; fences = fenceChoices;
            Text = "Add a rule"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; TopMost = true;
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.None; Icon = app.AppIcon;
            ClientSize = new Size((int)(460 * k), (int)(268 * k));
            BackColor = t.Dark ? Color.FromArgb(28, 30, 38) : Color.FromArgb(246, 247, 250);
            ForeColor = t.Dark ? Color.FromArgb(236, 238, 245) : Color.FromArgb(24, 26, 32);
            Font = Gfx.SafeFont(t.FontName, 13.5f * k, FontStyle.Regular);
            Color sub = t.Dark ? Color.FromArgb(150, 156, 172) : Color.FromArgb(104, 110, 124);
            int pad = (int)(20 * k), w = ClientSize.Width - 2 * pad;
            var l1 = new Label { Text = "Files whose name matches", Location = new Point(pad, pad), AutoSize = true };
            pat = new TextBox { Location = new Point(pad, l1.Bottom + (int)(6 * k)), Width = w, Text = "*.pdf", BorderStyle = BorderStyle.FixedSingle };
            example = new Label { Location = new Point(pad, pat.Bottom + (int)(4 * k)), Size = new Size(w, (int)(36 * k)), ForeColor = sub, Font = Gfx.SafeFont(t.FontName, 12 * k, FontStyle.Regular),
                Text = "Use * for anything: *.pdf, Screenshot*, *homework*. Plain words match any name containing them." };
            var l2 = new Label { Text = "go to this fence", Location = new Point(pad, example.Bottom + (int)(8 * k)), AutoSize = true };
            fence = new ComboBox { Location = new Point(pad, l2.Bottom + (int)(6 * k)), Width = w, DropDownStyle = ComboBoxStyle.DropDownList };
            int sel = 0;
            for (int i = 0; i < fences.Count; i++) { fence.Items.Add(fences[i].Value); if (fences[i].Key == preselect) sel = i; }
            if (fence.Items.Count > 0) fence.SelectedIndex = sel;
            var ok = new Button { Text = "Add rule", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = t.Accent, ForeColor = Color.White, Size = new Size((int)(110 * k), (int)(34 * k)) };
            ok.FlatAppearance.BorderSize = 0;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, Size = new Size((int)(96 * k), (int)(34 * k)) };
            ok.Location = new Point(ClientSize.Width - pad - ok.Width, ClientSize.Height - pad - ok.Height);
            cancel.Location = new Point(ok.Left - (int)(10 * k) - cancel.Width, ok.Top);
            Controls.AddRange(new Control[] { l1, pat, example, l2, fence, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
            Load += delegate { Native.SetDarkTitleBar(Handle, t.Dark); pat.SelectAll(); pat.Focus(); };
            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK) return;
                if (pat.Text.Trim().Length == 0 || fence.SelectedIndex < 0) { e.Cancel = true; return; }
                Pattern = pat.Text.Trim(); FenceId = fences[fence.SelectedIndex].Key;
            };
        }
    }
}
