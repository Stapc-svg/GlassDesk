// GlassDesk - desktop organizer: sorts desktop items into glass "fences",
// with drag & drop between fences, into folders, from / to File Explorer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace GlassDesk
{
    internal class DesktopItem
    {
        public const string RecycleBinPath = "::RecycleBin";
        public string Path, Name, Ext;
        public bool IsDir;
        public bool IsRecycleBin { get { return Path == RecycleBinPath; } }
        Bitmap icon; bool iconTried;

        public DesktopItem(string path, bool dir)
        {
            Path = path; IsDir = dir;
            if (path == RecycleBinPath) { Name = "Recycle Bin"; Ext = ""; IsDir = true; return; }
            Ext = dir ? "" : System.IO.Path.GetExtension(path).ToLowerInvariant();
            string n = System.IO.Path.GetFileName(path);
            if (Ext == ".lnk" || Ext == ".url" || Ext == ".appref-ms") n = System.IO.Path.GetFileNameWithoutExtension(path);
            Name = n;
        }

        public static DesktopItem RecycleBin() { return new DesktopItem(RecycleBinPath, true); }

        public Bitmap Icon
        {
            get
            {
                if (!iconTried)
                {
                    iconTried = true;
                    icon = IsRecycleBin ? Native.GetStockIcon(Native.RecycleBinFull() ? 32u : 31u) : Native.GetShellIcon(Path);
                }
                return icon;
            }
        }
        public void ResetIcon() { Dispose(); iconTried = false; }
        public void Dispose() { if (icon != null) { icon.Dispose(); icon = null; } }

        /// <summary>Folder that directly contains this item ("" for the Recycle Bin).</summary>
        public string Parent { get { return IsRecycleBin ? "" : (System.IO.Path.GetDirectoryName(Path) ?? ""); } }
    }

    internal static class Organizer
    {
        public static readonly string[][] Categories =
        {
            new[] { "apps", "Apps & Shortcuts", ".lnk .url .exe .appref-ms .msi .bat .cmd" },
            new[] { "folders", "Folders", "" },
            new[] { "docs", "Documents", ".pdf .doc .docx .txt .rtf .odt .md .xls .xlsx .csv .ppt .pptx .odp .ods .epub .one .html .htm" },
            new[] { "images", "Images", ".png .jpg .jpeg .gif .bmp .webp .svg .ico .heic .tif .tiff .psd .avif" },
            new[] { "media", "Music & Video", ".mp3 .wav .flac .m4a .ogg .aac .mp4 .mkv .avi .mov .wmv .webm" },
            new[] { "archives", "Archives", ".zip .rar .7z .tar .gz .iso .cab .xz" },
            new[] { "other", "Other files", "" },
        };

        public static string CategoryOf(DesktopItem it)
        {
            if (it.IsDir) return "folders";
            foreach (var c in Categories)
                if (c[2].Length > 0 && (" " + c[2] + " ").Contains(" " + it.Ext + " ")) return c[0];
            return "other";
        }

        public static string CategoryName(string key)
        {
            foreach (var c in Categories) if (c[0] == key) return c[1];
            return key;
        }

        public static string UserDesktop
        {
            get { try { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } catch { return ""; } }
        }

        public static string[] DesktopFolders()
        {
            var list = new List<string>();
            list.Add(UserDesktop);
            try { list.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)); } catch { }
            list.RemoveAll(p => string.IsNullOrEmpty(p) || !Directory.Exists(p));
            return list.ToArray();
        }

        public static bool IsOnDesktop(string path)
        {
            string parent;
            try { parent = Path.GetDirectoryName(path); } catch { return false; }
            foreach (string d in DesktopFolders()) if (SamePath(parent, d)) return true;
            return false;
        }

        public static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True if <paramref name="child"/> is <paramref name="folder"/> or inside it.</summary>
        public static bool IsInside(string child, string folder)
        {
            if (SamePath(child, folder)) return true;
            string f = folder.TrimEnd('\\', '/') + "\\";
            return child.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        public static List<DesktopItem> List(string folder)
        {
            var items = new List<DesktopItem>();
            try
            {
                var di = new DirectoryInfo(folder);
                foreach (var d in di.GetDirectories())
                    if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) items.Add(new DesktopItem(d.FullName, true));
                foreach (var f in di.GetFiles())
                    if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        items.Add(new DesktopItem(f.FullName, false));
            }
            catch { }
            return items;
        }

        public static string Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s.ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }
    }

    internal enum FenceKind { Category, Custom, Folder }

    internal class FenceWindow : GlassWindow
    {
        public const string ItemFormat = "GlassDeskItem";

        public readonly string Key;        // category key, custom fence id, or folder path
        public readonly FenceKind Kind;
        public bool IsFolder { get { return Kind == FenceKind.Folder; } }
        /// <summary>Category and custom fences hold desktop items; folder fences show a folder.</summary>
        public bool HoldsDesktopItems { get { return Kind != FenceKind.Folder; } }
        public string Caption;
        public List<DesktopItem> Items = new List<DesktopItem>();
        int scroll, hover = -1;
        bool hoverTitle;

        // drag & drop state
        DesktopItem pressItem; Point pressPt; bool suppressClick;
        int dropTile = -1; bool dropFence;

        public FenceWindow(App app, string id, string key, string caption, FenceKind kind) : base(app, id)
        {
            Key = key; Caption = caption; Kind = kind;
            Resizable = true;
            MinPanel = new Size(170, 110);
            AllowDrop = true;
        }

        bool Collapsed { get { return Cfg.Collapsed.Contains(Id); } }
        int TitleH { get { return T.S(40); } }
        int TileW { get { return T.S(84); } }
        int TileH { get { return T.S(84); } }
        int Gap { get { return T.S(10); } }

        public override Size PreferredPanelSize(Size saved)
        {
            Size s = saved;
            if (s.Width <= 0)
            {
                int w = T.S(372);
                int cols = Math.Max(1, (w - 2 * Gap) / TileW);
                int rows = Math.Max(1, Math.Min(3, (Items.Count + cols - 1) / cols));
                s = new Size(w, TitleH + rows * TileH + Gap);
            }
            if (Collapsed) s.Height = TitleH;
            return s;
        }

        public void SetItems(List<DesktopItem> items)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in items) keep.Add(n.Path);
            foreach (var old in Items) if (!keep.Contains(old.Path)) old.Dispose();
            // keep already-loaded icons
            var map = new Dictionary<string, DesktopItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Items) map[o.Path] = o;
            var result = new List<DesktopItem>();
            foreach (var n in items)
            {
                DesktopItem o;
                if (map.TryGetValue(n.Path, out o)) { if (o.IsRecycleBin) o.ResetIcon(); result.Add(o); }
                else result.Add(n);
            }
            result.Sort((a, b) =>
            {
                if (a.IsRecycleBin != b.IsRecycleBin) return a.IsRecycleBin ? -1 : 1;
                if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            Items = result;
            ClampScroll();
        }

        Rectangle Content { get { return new Rectangle(Panel.X + Gap, Panel.Y + TitleH, Panel.Width - 2 * Gap, Panel.Height - TitleH - Gap / 2); } }
        int Cols { get { return Math.Max(1, Content.Width / TileW); } }
        int ContentHeight { get { int rows = (Items.Count + Cols - 1) / Cols; return rows * TileH; } }

        void ClampScroll()
        {
            int max = Math.Max(0, ContentHeight - Content.Height);
            scroll = Math.Max(0, Math.Min(scroll, max));
        }

        Rectangle TileRect(int i)
        {
            Rectangle c = Content;
            int cols = Cols;
            int gap = (c.Width - cols * TileW) / 2;
            return new Rectangle(c.X + gap + (i % cols) * TileW, c.Y + (i / cols) * TileH - scroll, TileW, TileH);
        }

        int HitItem(Point p)
        {
            if (Collapsed || !Content.Contains(p)) return -1;
            for (int i = 0; i < Items.Count; i++) if (TileRect(i).Contains(p)) return i;
            return -1;
        }

        protected override bool IsDragArea(Point p) { return p.Y < Panel.Y + TitleH; }

        // ================================================================ painting
        protected override void PaintContent(Graphics g)
        {
            var titleR = new Rectangle(Panel.X + T.S(14), Panel.Y, Panel.Width - T.S(28), TitleH);
            float dot = T.Sf(8);
            using (var b = new SolidBrush(T.Accent))
            {
                if (Kind == FenceKind.Custom) g.FillRectangle(b, titleR.X, titleR.Y + (TitleH - dot) / 2, dot, dot);
                else g.FillEllipse(b, titleR.X, titleR.Y + (TitleH - dot) / 2, dot, dot);
            }
            var capR = new Rectangle(titleR.X + T.S(16), titleR.Y, titleR.Width - T.S(70), TitleH);
            using (var b = new SolidBrush(T.Text)) g.DrawString(Caption, Gfx.F(T.FontName, T.Sf(13.5f), FontStyle.Bold), b, capR, Gfx.Left);
            using (var b = new SolidBrush(T.SubText))
                g.DrawString(Items.Count.ToString(), Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular), b, new Rectangle(titleR.Right - T.S(56), titleR.Y, T.S(34), TitleH), Gfx.Right);
            DrawChevron(g, new PointF(titleR.Right - T.Sf(8), titleR.Y + TitleH / 2f), Collapsed, hoverTitle ? T.Text : T.SubText);

            if (dropFence)
                using (var path = Gfx.Round(new RectangleF(Panel.X + 1.5f, Panel.Y + 1.5f, Panel.Width - 3, Panel.Height - 3), Math.Max(2, T.S(Cfg.Radius) - 1.5f)))
                using (var pen = new Pen(T.Accent, T.Sf(2.5f))) g.DrawPath(pen, path);
            if (Collapsed) return;

            using (var pen = new Pen(Gfx.WithAlpha(T.Text, 26), 1))
                g.DrawLine(pen, Panel.X + T.S(12), Panel.Y + TitleH - 1, Panel.Right - T.S(12), Panel.Y + TitleH - 1);

            Rectangle c = Content;
            var state = g.Save();
            g.SetClip(new Rectangle(c.X - 2, c.Y, c.Width + 4, c.Height));
            if (Items.Count == 0)
            {
                string empty = IsFolder ? "This folder is empty" : Kind == FenceKind.Custom ? "Drag items here" : "Nothing here yet";
                using (var b = new SolidBrush(T.SubText))
                    g.DrawString(empty, Gfx.F(T.FontName, T.Sf(12.5f), FontStyle.Regular), b, c, Gfx.Center);
            }
            int iconSize = T.S(36);
            var label = Gfx.F(T.FontName, T.Sf(12), FontStyle.Regular);
            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle r = TileRect(i);
                if (r.Bottom < c.Top || r.Top > c.Bottom) continue;
                var tile = new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
                if (i == dropTile)
                {
                    Gfx.FillRound(g, Gfx.WithAlpha(T.Accent, 80), tile, T.Sf(10));
                    using (var p = Gfx.Round(tile, T.Sf(10))) using (var pen = new Pen(T.Accent, T.Sf(2))) g.DrawPath(pen, p);
                }
                else if (i == hover) Gfx.FillRound(g, T.Hover, tile, T.Sf(10));
                var ir = new Rectangle(r.X + (r.Width - iconSize) / 2, r.Y + T.S(8), iconSize, iconSize);
                Bitmap icon = Items[i].Icon;
                if (icon != null) g.DrawImage(icon, ir);
                else DrawFallbackIcon(g, ir, Items[i]);
                var lr = new RectangleF(r.X + T.S(4), ir.Bottom + T.S(4), r.Width - T.S(8), T.Sf(32));
                if (T.Dark)
                    using (var sb = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                        g.DrawString(Items[i].Name, label, sb, new RectangleF(lr.X, lr.Y + 1, lr.Width, lr.Height), Gfx.TopCenterWrap);
                using (var b = new SolidBrush(T.Text)) g.DrawString(Items[i].Name, label, b, lr, Gfx.TopCenterWrap);
            }
            g.Restore(state);

            int total = ContentHeight;
            if (total > c.Height)
            {
                float h = Math.Max(T.Sf(24), c.Height * (float)c.Height / total);
                float y = c.Y + (c.Height - h) * scroll / (float)(total - c.Height);
                Gfx.FillRound(g, Gfx.WithAlpha(T.Text, 70), new RectangleF(Panel.Right - T.Sf(6), y, T.Sf(3), h), T.Sf(1.5f));
            }
            using (var b = new SolidBrush(Gfx.WithAlpha(T.Text, 60)))
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j <= i; j++)
                        g.FillEllipse(b, Panel.Right - T.Sf(8) - j * T.Sf(4), Panel.Bottom - T.Sf(8) - (i - j) * T.Sf(4), T.Sf(2), T.Sf(2));
        }

        void DrawChevron(Graphics g, PointF c, bool down, Color col)
        {
            float s = T.Sf(4);
            using (var pen = new Pen(col, T.Sf(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (down) g.DrawLines(pen, new[] { new PointF(c.X - s, c.Y - s / 2), new PointF(c.X, c.Y + s / 2), new PointF(c.X + s, c.Y - s / 2) });
                else g.DrawLines(pen, new[] { new PointF(c.X - s, c.Y + s / 2), new PointF(c.X, c.Y - s / 2), new PointF(c.X + s, c.Y + s / 2) });
            }
        }

        static readonly Color[] fallbackColors = { Color.FromArgb(94, 129, 244), Color.FromArgb(46, 184, 126), Color.FromArgb(240, 160, 60), Color.FromArgb(220, 90, 110), Color.FromArgb(150, 110, 230), Color.FromArgb(70, 170, 200) };
        void DrawFallbackIcon(Graphics g, Rectangle r, DesktopItem it)
        {
            if (it.IsRecycleBin)
            {
                var body = new RectangleF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.22f, r.Width * 0.6f, r.Height * 0.72f);
                Gfx.FillRound(g, Color.FromArgb(235, 200, 214, 235), body, r.Width * 0.08f);
                Gfx.FillRound(g, Color.FromArgb(255, 150, 170, 200), new RectangleF(r.X + r.Width * 0.14f, r.Y + r.Height * 0.12f, r.Width * 0.72f, r.Height * 0.1f), r.Width * 0.04f);
                return;
            }
            string cat = Organizer.CategoryOf(it);
            int idx = Array.FindIndex(Organizer.Categories, c => c[0] == cat);
            Color col = fallbackColors[Math.Max(0, idx) % fallbackColors.Length];
            if (it.IsDir)
            {
                Gfx.FillRound(g, Color.FromArgb(255, 240, 190, 70), new RectangleF(r.X, r.Y + r.Height * 0.12f, r.Width * 0.5f, r.Height * 0.3f), r.Width * 0.08f);
                Gfx.FillRound(g, Color.FromArgb(255, 252, 208, 96), new RectangleF(r.X, r.Y + r.Height * 0.22f, r.Width, r.Height * 0.68f), r.Width * 0.1f);
                return;
            }
            var rr = new RectangleF(r.X + r.Width * 0.12f, r.Y, r.Width * 0.76f, r.Height);
            Gfx.FillRound(g, col, rr, r.Width * 0.12f);
            string t = it.Ext.TrimStart('.').ToUpperInvariant();
            if (t.Length > 4) t = t.Substring(0, 4);
            using (var b = new SolidBrush(Color.White)) g.DrawString(t, Gfx.F(T.FontName, r.Height * 0.24f, FontStyle.Bold), b, rr, Gfx.Center);
        }

        // ================================================================ mouse
        protected override void OnHover(Point p)
        {
            int h = HitItem(p);
            bool ht = p.Y >= 0 && p.Y < Panel.Y + TitleH && Panel.Contains(p);
            if (h != hover || ht != hoverTitle) { hover = h; hoverTitle = ht; Redraw(); }
        }

        protected override void OnWheel(int delta)
        {
            if (Collapsed) return;
            int old = scroll;
            scroll -= Math.Sign(delta) * TileH / 2;
            ClampScroll();
            if (old != scroll) Redraw();
        }

        protected override void OnResized() { ClampScroll(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            pressItem = null;
            if (e.Button == MouseButtons.Left && e.Y >= Panel.Y + TitleH && !InGrip(e.Location))
            {
                int i = HitItem(e.Location);
                if (i >= 0 && !Items[i].IsRecycleBin) { pressItem = Items[i]; pressPt = e.Location; }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pressItem != null && (e.Button & MouseButtons.Left) != 0)
            {
                Size ds = SystemInformation.DragSize;
                if (Math.Abs(e.X - pressPt.X) > ds.Width || Math.Abs(e.Y - pressPt.Y) > ds.Height)
                {
                    var it = pressItem; pressItem = null;
                    StartDrag(it);
                    return;
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressItem = null;
            base.OnMouseUp(e);
        }

        void StartDrag(DesktopItem it)
        {
            var data = new DataObject();
            var files = new System.Collections.Specialized.StringCollection();
            files.Add(it.Path);
            data.SetFileDropList(files);
            data.SetData(ItemFormat, it.Path);
            hover = -1; Redraw();
            try { DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch (Exception ex) { Program.Log(ex); }
            // if OLE swallowed the button-up, there is no click to suppress
            suppressClick = (Control.MouseButtons & MouseButtons.Left) != 0;
            App.RescanSoon();
        }

        protected override void OnClickAt(Point p, MouseButtons b)
        {
            if (suppressClick) { suppressClick = false; return; }
            if (p.Y < Panel.Y + TitleH)
            {
                if (b == MouseButtons.Right) ShowFenceMenu();
                else if (p.X > Panel.Right - T.S(36)) ToggleCollapsed();
                return;
            }
            int i = HitItem(p);
            if (b == MouseButtons.Right)
            {
                if (i >= 0) ShowItemMenu(Items[i]); else ShowFenceMenu();
            }
            else if (b == MouseButtons.Left && i >= 0 && Cfg.SingleClickOpen) Open(Items[i]);
        }

        protected override void OnDoubleClickAt(Point p)
        {
            if (p.Y < Panel.Y + TitleH) { ToggleCollapsed(); return; }
            int i = HitItem(p);
            if (i >= 0 && !Cfg.SingleClickOpen) Open(Items[i]);
        }

        void ToggleCollapsed()
        {
            if (Collapsed) Cfg.Collapsed.Remove(Id); else Cfg.Collapsed.Add(Id);
            Cfg.Save();
            App.PlaceWindow(this);
        }

        static void Open(DesktopItem it)
        {
            try
            {
                if (it.IsRecycleBin) Process.Start("explorer.exe", "shell:RecycleBinFolder");
                else Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("Could not open \"" + it.Name + "\".\n\n" + ex.Message, "GlassDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // ================================================================ drag & drop (target)
        enum DropAct { None, Pin, ToDesktopAndPin, ToFolder, Recycle }

        static string[] DroppedPaths(IDataObject d)
        {
            try
            {
                if (d != null && d.GetDataPresent(DataFormats.FileDrop)) return d.GetData(DataFormats.FileDrop) as string[];
            }
            catch { }
            return null;
        }

        DropAct Plan(DragEventArgs e, string[] paths, out int tileIndex, out string dest)
        {
            tileIndex = -1; dest = null;
            if (paths == null || paths.Length == 0) return DropAct.None;
            Point p = PointToClient(new Point(e.X, e.Y));
            int i = HitItem(p);
            if (i >= 0)
            {
                DesktopItem t = Items[i];
                if (t.IsRecycleBin) { tileIndex = i; return DropAct.Recycle; }
                if (t.IsDir)
                {
                    bool ok = true;
                    foreach (string s in paths)
                        if (Organizer.IsInside(t.Path, s) || Organizer.SamePath(Path.GetDirectoryName(s), t.Path)) { ok = false; break; }
                    if (ok) { tileIndex = i; dest = t.Path; return DropAct.ToFolder; }
                }
            }
            if (IsFolder)
            {
                bool any = false;
                foreach (string s in paths)
                    if (!Organizer.SamePath(Path.GetDirectoryName(s), Key) && !Organizer.IsInside(Key, s)) { any = true; break; }
                if (!any) return DropAct.None;
                dest = Key;
                return DropAct.ToFolder;
            }
            // category or custom fence: desktop items are just re-assigned; others are brought to the desktop
            bool allOnDesktop = true, anyNew = false;
            foreach (string s in paths)
            {
                if (!Organizer.IsOnDesktop(s)) allOnDesktop = false;
                if (!Items.Exists(x => Organizer.SamePath(x.Path, s))) anyNew = true;
            }
            if (allOnDesktop) return anyNew ? DropAct.Pin : DropAct.None;
            dest = Organizer.UserDesktop;
            return DropAct.ToDesktopAndPin;
        }

        static DragDropEffects Effect(DragEventArgs e, DropAct act)
        {
            if (act == DropAct.None) return DragDropEffects.None;
            DragDropEffects want;
            if (act == DropAct.Pin) want = DragDropEffects.Link;
            else if (act == DropAct.Recycle) want = DragDropEffects.Move;
            else want = (e.KeyState & 8) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;   // Ctrl = copy
            foreach (var f in new[] { want, DragDropEffects.Move, DragDropEffects.Copy, DragDropEffects.Link })
                if ((e.AllowedEffect & f) != 0) return f;
            return DragDropEffects.None;
        }

        void SetDropHighlight(int tile, bool fence)
        {
            if (tile == dropTile && fence == dropFence) return;
            dropTile = tile; dropFence = fence;
            Redraw();
        }

        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); UpdateDrag(e); }
        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); UpdateDrag(e); }
        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); SetDropHighlight(-1, false); }

        void UpdateDrag(DragEventArgs e)
        {
            int tile; string dest;
            DropAct act = Plan(e, DroppedPaths(e.Data), out tile, out dest);
            e.Effect = Effect(e, act);
            if (e.Effect == DragDropEffects.None) SetDropHighlight(-1, false);
            else SetDropHighlight(tile, tile < 0);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            string[] paths = DroppedPaths(e.Data);
            int tile; string dest;
            DropAct act = Plan(e, paths, out tile, out dest);
            DragDropEffects eff = Effect(e, act);
            SetDropHighlight(-1, false);
            if (eff == DragDropEffects.None) { e.Effect = DragDropEffects.None; return; }
            bool copy = eff == DragDropEffects.Copy;
            // We do the file work ourselves, so tell the source "copy" / "link" — it must never delete anything.
            e.Effect = act == DropAct.Pin ? (e.AllowedEffect & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy : DragDropEffects.Copy;
            string[] list = (string[])paths.Clone();
            // finish after the drag has fully ended, so File Explorer isn't kept waiting
            BeginInvoke(new Action(delegate { Execute(act, list, dest, copy); }));
        }

        void Execute(DropAct act, string[] paths, string dest, bool copy)
        {
            try
            {
                switch (act)
                {
                    case DropAct.Pin:
                        foreach (string s in paths) App.Pin(s, Id);
                        Cfg.Save(); App.Rebuild();
                        break;
                    case DropAct.ToDesktopAndPin:
                        if (Native.ShellTransfer(Handle, paths, dest, copy))
                        {
                            foreach (string s in paths) App.Pin(Path.Combine(dest, Path.GetFileName(s.TrimEnd('\\'))), Id);
                            Cfg.Save();
                        }
                        App.RescanSoon();
                        break;
                    case DropAct.ToFolder:
                        if (Native.ShellTransfer(Handle, paths, dest, copy) && !copy)
                        {
                            foreach (string s in paths) App.Unpin(s);
                            Cfg.Save();
                        }
                        App.RescanSoon();
                        break;
                    case DropAct.Recycle:
                        RecycleWithConfirm(paths);
                        break;
                }
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        void RecycleWithConfirm(string[] paths)
        {
            string what = paths.Length == 1 ? "\"" + Path.GetFileName(paths[0].TrimEnd('\\')) + "\"" : paths.Length + " items";
            if (MessageBox.Show("Move " + what + " to the Recycle Bin?", "GlassDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (string s in paths)
                if (!Native.RecycleFile(s)) MessageBox.Show("Could not delete \"" + Path.GetFileName(s.TrimEnd('\\')) + "\".", "GlassDesk");
            App.RescanSoon();
        }

        // ================================================================ menus
        void ShowItemMenu(DesktopItem it)
        {
            var m = App.NewMenu();
            m.Items.Add("Open", null, delegate { Open(it); });
            if (it.IsRecycleBin)
            {
                m.Items.Add("Empty Recycle Bin", null, delegate { Native.EmptyRecycleBin(Handle); App.RescanSoon(); });
            }
            else
            {
                m.Items.Add("Show in File Explorer", null, delegate { try { Process.Start("explorer.exe", "/select,\"" + it.Path + "\""); } catch { } });
                m.Items.Add("Copy path", null, delegate { try { Clipboard.SetText(it.Path); } catch { } });
            }

            // move to another fence
            var move = new ToolStripMenuItem("Move to fence");
            foreach (var f in App.DesktopFences())
            {
                if (f == this) continue;
                var target = f;
                move.DropDownItems.Add(target.Caption, null, delegate { App.MoveToFence(it.Path, target); });
            }
            if (move.DropDownItems.Count > 0) move.DropDownItems.Add(new ToolStripSeparator());
            move.DropDownItems.Add("New fence…", null, delegate { var nf = App.NewFence(); if (nf != null) App.MoveToFence(it.Path, nf); });
            m.Items.Add(move);
            if (Cfg.Pins.ContainsKey(it.Path))
            {
                string home = Organizer.CategoryName(Organizer.CategoryOf(it));
                m.Items.Add("Back to " + home, null, delegate { App.Unpin(it.Path); Cfg.Save(); App.Rebuild(); });
            }

            if (!it.IsRecycleBin)
            {
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Delete (Recycle Bin)", null, delegate { RecycleWithConfirm(new[] { it.Path }); });
                m.Items.Add("Properties", null, delegate { Native.ShowProperties(it.Path); });
            }
            m.Show(Cursor.Position);
        }

        void ShowFenceMenu()
        {
            var m = App.NewMenu();
            m.Items.Add(Collapsed ? "Expand" : "Roll up", null, delegate { ToggleCollapsed(); });
            m.Items.Add("Rename…", null, delegate
            {
                string name = InputBox.Ask("Rename fence", Caption, App.Theme);
                if (name == null) return;
                if (name.Trim().Length == 0 && Kind != FenceKind.Custom) Cfg.Titles.Remove(Id);
                else if (name.Trim().Length > 0) Cfg.Titles[Id] = name.Trim();
                Cfg.Save(); App.Rebuild();
            });
            m.Items.Add("New fence…", null, delegate { App.NewFence(); });
            if (Kind == FenceKind.Folder)
            {
                m.Items.Add("Open folder", null, delegate { try { Process.Start("explorer.exe", "\"" + Key + "\""); } catch { } });
                m.Items.Add("Remove this fence", null, delegate { Cfg.FolderFences.Remove(Key); Cfg.Save(); App.Rebuild(); });
            }
            else if (Kind == FenceKind.Custom)
            {
                m.Items.Add("Delete this fence", null, delegate { App.DeleteCustomFence(Id); });
            }
            else
            {
                m.Items.Add("Open Desktop folder", null, delegate { try { Process.Start("explorer.exe", "\"" + Organizer.UserDesktop + "\""); } catch { } });
                m.Items.Add("Hide this category", null, delegate { Cfg.Categories[Key] = false; Cfg.Save(); App.Rebuild(); });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Settings…", null, delegate { App.ShowSettings(3); });
            m.Show(Cursor.Position);
        }
    }
}
