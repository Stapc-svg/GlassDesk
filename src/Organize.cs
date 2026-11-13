// GlassDesk 1.8 - "Organize desktop": see where every file will go and tick the ones to sort.
// Files you untick go to an "Unsorted" fence, so nothing is hidden or lost.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GlassDesk
{
    internal class OrganizeForm : GlassForm
    {
        public class Group { public string Id, Title; public List<DesktopItem> Items = new List<DesktopItem>(); }

        readonly List<Group> groups;
        readonly HashSet<DesktopItem> on = new HashSet<DesktopItem>();
        readonly List<KeyValuePair<RectangleF, Action>> hits = new List<KeyValuePair<RectangleF, Action>>();
        readonly Action<HashSet<DesktopItem>, List<DesktopItem>> apply;
        int scroll, contentH;
        Point mouse = new Point(-1, -1);
        Point dragFrom; bool dragging;

        public OrganizeForm(App app, List<Group> groups, HashSet<DesktopItem> ticked, Action<HashSet<DesktopItem>, List<DesktopItem>> apply) : base(app)
        {
            this.groups = groups; this.apply = apply;
            foreach (var it in ticked) on.Add(it);
            ShowInTaskbar = true; TopMost = true;
            if (app.AppIcon != null) Icon = app.AppIcon;
            int w = (int)(900 * k), h = (int)(640 * k);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            w = Math.Min(w, wa.Width - 40); h = Math.Min(h, wa.Height - 40);
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        }

        int Pad { get { return (int)(26 * k); } }
        int HeadH { get { return (int)(96 * k); } }
        int FootTop { get { return Height - (int)(66 * k); } }
        Rectangle Body { get { return new Rectangle(Pad, HeadH, Width - 2 * Pad, FootTop - HeadH - (int)(8 * k)); } }
        int Total { get { int n = 0; foreach (var g in groups) n += g.Items.Count; return n; } }

        void Hit(RectangleF r, Action a) { hits.Add(new KeyValuePair<RectangleF, Action>(r, a)); }

        protected override void PaintAll(Graphics g)
        {
            hits.Clear();
            PaintGlass(g);
            // header
            using (var b = new SolidBrush(P.Text)) g.DrawString("Organize desktop", Gfx.F(T.DisplayFont, 24 * k, FontStyle.Bold), b, new RectangleF(Pad, 22 * k, Width - 2 * Pad, 34 * k), Gfx.Left);
            using (var b = new SolidBrush(P.Sub))
                g.DrawString("Ticked files go into their fence. Unticked files go to an \"Unsorted\" fence.", Gfx.F(T.FontName, 13.5f * k, FontStyle.Regular), b, new RectangleF(Pad, 56 * k, Width - 2 * Pad - 60 * k, 22 * k), Gfx.Left);
            var cr = new RectangleF(Width - 50 * k, 16 * k, 36 * k, 36 * k);
            Close(g, cr, cr.Contains(mouse)); Hit(cr, delegate { Close(); });

            // cards in two columns, each card goes in the shorter column
            var body = Body;
            var st = g.Save();
            g.SetClip(body);
            float gap = 16 * k, cw = (body.Width - gap - 8 * k) / 2f;
            float[] colY = { body.Y, body.Y };
            var fName = Gfx.F(T.FontName, 11 * k, FontStyle.Regular);
            float tileW = 82 * k, tileH = 82 * k;
            int perRow = Math.Max(1, (int)((cw - 24 * k) / tileW));
            foreach (var grp in groups)
            {
                if (grp.Items.Count == 0) continue;
                int col = colY[0] <= colY[1] ? 0 : 1;
                int rows = (grp.Items.Count + perRow - 1) / perRow;
                float ch = 56 * k + rows * tileH + 12 * k;
                var card = new RectangleF(body.X + col * (cw + gap), colY[col] - scroll, cw, ch);
                colY[col] += ch + gap;
                if (card.Bottom < body.Y || card.Y > body.Bottom) continue;
                Chip(g, card, 14 * k, false);
                // header: tick-all box, name, count
                int n = 0; foreach (var it in grp.Items) if (on.Contains(it)) n++;
                var box = new RectangleF(card.X + 16 * k, card.Y + 18 * k, 20 * k, 20 * k);
                DrawCheck(g, box, n == grp.Items.Count ? 2 : n > 0 ? 1 : 0, true);
                var gg = grp;
                Hit(new RectangleF(card.X, card.Y, card.Width, 52 * k), delegate { bool all = true; foreach (var it in gg.Items) if (!on.Contains(it)) all = false; foreach (var it in gg.Items) { if (all) on.Remove(it); else on.Add(it); } Invalidate(); });
                using (var b = new SolidBrush(P.Text)) g.DrawString(grp.Title, Gfx.F(T.FontName, 14 * k, FontStyle.Bold), b, new RectangleF(box.Right + 12 * k, card.Y + 12 * k, card.Width - 170 * k, 32 * k), Gfx.Left);
                string sel = n + " / " + grp.Items.Count + " selected";
                var pill = new RectangleF(card.Right - 120 * k, card.Y + 16 * k, 104 * k, 24 * k);
                Gfx.FillRound(g, Gfx.WithAlpha(P.Accent, T.Dark ? 60 : 40), pill, 12 * k);
                using (var b = new SolidBrush(P.Text)) g.DrawString(sel, Gfx.F(T.FontName, 11 * k, FontStyle.Regular), b, pill, Gfx.Center);
                // tiles
                float gx = card.X + (card.Width - perRow * tileW) / 2;
                for (int i = 0; i < grp.Items.Count; i++)
                {
                    var it = grp.Items[i];
                    var t = new RectangleF(gx + (i % perRow) * tileW, card.Y + 52 * k + (i / perRow) * tileH, tileW, tileH);
                    bool isOn = on.Contains(it), hot = t.Contains(mouse);
                    var inner = RectangleF.Inflate(t, -3 * k, -3 * k);
                    if (isOn) Gfx.FillRound(g, Gfx.WithAlpha(P.Accent, T.Dark ? 34 : 26), inner, 10 * k);
                    if (hot) Gfx.FillRound(g, T.Dark ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(14, 0, 0, 0), inner, 10 * k);
                    int isz = (int)(34 * k);
                    var ir = new Rectangle((int)(t.X + (t.Width - isz) / 2), (int)(t.Y + 10 * k), isz, isz);
                    var icon = it.Icon;
                    if (icon != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        if (isOn) g.DrawImage(icon, ir);
                        else using (var ia = new System.Drawing.Imaging.ImageAttributes())
                            {
                                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.4f };
                                ia.SetColorMatrix(cm);
                                g.DrawImage(icon, ir, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                            }
                    }
                    else
                        using (var p = Gfx.Round(ir, 8 * k)) using (var b = new SolidBrush(Gfx.WithAlpha(P.Accent, isOn ? 220 : 90))) g.FillPath(b, p);
                    using (var b = new SolidBrush(isOn ? P.Text : P.Sub))
                        g.DrawString(it.Name, fName, b, new RectangleF(t.X + 4 * k, ir.Bottom + 4 * k, t.Width - 8 * k, 30 * k), Gfx.TopCenterWrap);
                    DrawCheck(g, new RectangleF(t.Right - 22 * k, t.Y + 5 * k, 16 * k, 16 * k), isOn ? 2 : 0, false);
                    var item = it;
                    Hit(t, delegate { if (!on.Remove(item)) on.Add(item); Invalidate(); });
                }
            }
            contentH = (int)(Math.Max(colY[0], colY[1]) - body.Y);
            g.Restore(st);
            int max = Math.Max(0, contentH - body.Height);
            if (max > 0)
            {
                float th = Math.Max(30 * k, body.Height * (float)body.Height / contentH);
                float y = body.Y + (body.Height - th) * scroll / max;
                Gfx.FillRound(g, Gfx.WithAlpha(P.Text, 60), new RectangleF(Width - Pad / 2f - 3 * k, y, 4 * k, th), 2 * k);
            }

            // footer
            using (var pen = new Pen(T.Dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0))) g.DrawLine(pen, Pad, FootTop, Width - Pad, FootTop);
            int fences = 0; foreach (var gr in groups) if (gr.Items.Count > 0) fences++;
            using (var b = new SolidBrush(P.Sub))
                g.DrawString(fences + " fences  ·  " + Total + " files  ·  " + (Total - on.Count) + " stay unsorted", Gfx.F(T.FontName, 12.5f * k, FontStyle.Regular), b, new RectangleF(Pad, FootTop, Width / 2f, Height - FootTop), Gfx.Left);
            var fB = Gfx.F(T.FontName, 13 * k, FontStyle.Bold);
            float bh = 40 * k, by = FootTop + (Height - FootTop - bh) / 2;
            var go = new RectangleF(Width - Pad - 210 * k, by, 210 * k, bh);
            var cancel = new RectangleF(go.X - 12 * k - 110 * k, by, 110 * k, bh);
            Chip(g, cancel, bh / 2, cancel.Contains(mouse));
            using (var b = new SolidBrush(P.Text)) g.DrawString("Cancel", fB, b, cancel, Gfx.Center);
            AccentButton(g, go, "Start organizing · " + on.Count, fB, go.Contains(mouse));
            Hit(cancel, delegate { Close(); });
            Hit(go, delegate
            {
                var off = new List<DesktopItem>();
                foreach (var gr in groups) foreach (var it in gr.Items) if (!on.Contains(it)) off.Add(it);
                Close(); apply(on, off);
            });
        }

        /// <summary>state: 0 empty, 1 some, 2 ticked.</summary>
        void DrawCheck(Graphics g, RectangleF r, int state, bool square)
        {
            float rad = square ? 5 * k : r.Width / 2;
            using (var p = Gfx.Round(r, rad))
            {
                if (state > 0) using (var b = new LinearGradientBrush(r, P.Accent, P.Accent2, 45f)) g.FillPath(b, p);
                else
                {
                    using (var b = new SolidBrush(T.Dark ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(160, 255, 255, 255))) g.FillPath(b, p);
                    using (var pen = new Pen(Gfx.WithAlpha(P.Text, 120), 1.4f * k)) g.DrawPath(pen, p);
                }
            }
            using (var pen = new Pen(Color.White, 1.8f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (state == 2) g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.27f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.68f), new PointF(r.X + r.Width * 0.74f, r.Y + r.Height * 0.34f) });
                else if (state == 1) g.DrawLine(pen, r.X + r.Width * 0.3f, r.Y + r.Height / 2, r.Right - r.Width * 0.3f, r.Y + r.Height / 2);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { Location = new Point(Left + e.X - dragFrom.X, Top + e.Y - dragFrom.Y); return; }
            mouse = e.Location; Invalidate();
            bool any = false; foreach (var h in hits) if (h.Key.Contains(e.Location)) any = true;
            Cursor = any ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < HeadH && e.X < Width - 60 * k) { dragging = true; dragFrom = e.Location; }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Invalidate(); return; }
            if (e.Button != MouseButtons.Left) return;
            // the last hit added is drawn on top, so look from the end
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Key.Contains(e.Location) && Body.Contains(e.Location) == hits[i].Key.IntersectsWith(Body)) { hits[i].Value(); return; }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(Math.Max(0, contentH - Body.Height), scroll - e.Delta * (int)(80 * k) / 120));
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); mouse = new Point(-1, -1); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Invalidate(); }
    }
}
