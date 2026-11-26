// GlassDesk 2.2 - two desktop tricks:
//  • Double-click an empty spot on the desktop: every fence, widget and desktop icon fades away, for a
//    clean wallpaper (screenshots, video calls). Double-click again and they come back.
//  • Peek (Ctrl + Alt + Space): your fences and widgets come up on top of your open apps for a moment.
//    Pick a file, press Esc or click outside them, and they go back behind your windows.
// GlassDesk does not hook the mouse or change Explorer: it only checks, a few times a second, whether the
// left button went down over the desktop itself, and asks the desktop whether an icon is selected.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace GlassDesk
{
    internal sealed class DesktopDoubleClick : IDisposable
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);

        readonly App app;
        readonly Timer timer;
        bool wasDown, armed;
        int lastDownAt; Point lastDownPt;

        public DesktopDoubleClick(App app)
        {
            this.app = app;
            timer = new Timer { Interval = 25 };
            timer.Tick += delegate { Check(); };
        }
        public bool Enabled { get { return timer.Enabled; } set { if (value && Native.IsWindows) timer.Start(); else timer.Stop(); } }
        public void Dispose() { timer.Dispose(); }

        static string Cls(IntPtr h) { var sb = new StringBuilder(64); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }

        /// <summary>True when the point is on the Windows desktop itself (not on an app, the taskbar or a GlassDesk panel).</summary>
        static bool OnDesktop(Point p)
        {
            IntPtr h = WindowFromPoint(new POINT { x = p.X, y = p.Y });
            if (h == IntPtr.Zero) return false;
            string c = Cls(h);
            if (c == "SysListView32") { IntPtr par = GetParent(h); if (par == IntPtr.Zero || Cls(par) != "SHELLDLL_DefView") return false; }
            else if (c != "SHELLDLL_DefView" && c != "WorkerW" && c != "Progman") return false;
            string root = Cls(GetAncestor(h, 2 /* GA_ROOT */));
            return root == "Progman" || root == "WorkerW";
        }

        void Check()
        {
            int vk = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 2 : 1;
            bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
            if (down == wasDown) return;
            wasDown = down;
            Point p = Cursor.Position;
            if (down)
            {
                if (!OnDesktop(p)) { lastDownAt = 0; armed = false; return; }
                int now = Environment.TickCount;
                int dx = GetSystemMetrics(36 /* SM_CXDOUBLECLK */), dy = GetSystemMetrics(37);
                if (lastDownAt != 0 && unchecked(now - lastDownAt) <= GetDoubleClickTime() && Math.Abs(p.X - lastDownPt.X) <= dx && Math.Abs(p.Y - lastDownPt.Y) <= dy)
                { armed = true; lastDownAt = 0; }
                else { lastDownAt = now; lastDownPt = p; }
            }
            else if (armed)
            {
                armed = false;
                // a double-click on an icon opens it; only an empty spot (nothing selected) hides everything
                if (Native.DesktopIconSelected()) return;
                app.ToggleClean();
            }
        }
    }

    /// <summary>The dark see-through layer behind the panels while peeking. Clicking it ends the peek.</summary>
    internal sealed class PeekShade : Form
    {
        readonly Timer fade;
        public PeekShade(Action done)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black; Opacity = 0; TopMost = true; KeyPreview = true;
            Bounds = SystemInformation.VirtualScreen;
            Text = "GlassDesk peek";
            MouseDown += delegate { done(); };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) done(); };
            fade = new Timer { Interval = 15 };
            int start = Environment.TickCount;
            fade.Tick += delegate { double t = Math.Min(1, (Environment.TickCount - start) / 160.0); Opacity = 0.45 * t; if (t >= 1) fade.Stop(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e) { base.OnShown(e); fade.Start(); }
        protected override void Dispose(bool disposing) { if (disposing) fade.Dispose(); base.Dispose(disposing); }
    }
}
