// GlassDesk 1.3 - quick search (Ctrl + Alt + F) and the "add rule" dialog.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GlassDesk
{
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
