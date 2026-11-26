// GlassDesk 2.2 - Quick settings widget: volume, brightness, Wi-Fi, Bluetooth, dark mode and mute,
// right on the desktop, like Control Center on a Mac.
// Everything uses normal Windows features: the sound mixer (Core Audio), the screen's brightness
// (laptop screens through Windows, monitors through their own DDC/CI setting), the Wi-Fi and Bluetooth
// switches from Settings (Windows.Devices.Radios) and the light/dark setting. Slow calls run on a
// background thread so the desktop never stutters.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GlassDesk
{
    // ======================================================================== sound (Core Audio)
    internal static class Sound
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float db);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static Guid ctx = Guid.Empty;
        static IMMDeviceEnumerator en;
        /// <summary>The speakers or headphones Windows is using right now (asked again each time, so plugging in headphones just works).</summary>
        static IAudioEndpointVolume Endpoint()
        {
            if (!Native.IsWindows) return null;
            if (en == null) en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out dev) != 0 || dev == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID; object o;
            if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o) != 0) return null;
            return o as IAudioEndpointVolume;
        }
        /// <summary>Volume 0..1, or -1 when there is no sound device.</summary>
        public static float Get(out bool muted)
        {
            muted = false;
            try { var e = Endpoint(); if (e == null) return -1; float v; e.GetMasterVolumeLevelScalar(out v); e.GetMute(out muted); return v; }
            catch { return -1; }
        }
        public static void Set(float v)
        {
            try { var e = Endpoint(); if (e == null) return; e.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(1, v)), ref ctx); if (v > 0) e.SetMute(false, ref ctx); }
            catch { }
        }
        public static void SetMute(bool m) { try { var e = Endpoint(); if (e != null) e.SetMute(m, ref ctx); } catch { } }
    }

    // ======================================================================== brightness
    internal static class Brightness
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr mon, out uint n);
        [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr mon, uint n, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint v);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);

        /// <summary>0..100, or -1 when no screen lets GlassDesk change it. Slow (up to ~0.2 s): call it off the UI thread.</summary>
        public static int Get()
        {
            if (!Native.IsWindows) return -1;
            // laptop screens: Windows itself
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(new System.Management.ManagementScope(@"root\WMI"), new System.Management.SelectQuery("WmiMonitorBrightness")))
                    foreach (System.Management.ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            // desktop monitors: their own setting, through the cable (DDC/CI)
            int best = -1;
            Each(h => { uint mn, cur, mx; if (GetMonitorBrightness(h, out mn, out cur, out mx) && mx > mn && best < 0) best = (int)Math.Round(100.0 * (cur - mn) / (mx - mn)); });
            return best;
        }
        public static void Set(int pct)
        {
            if (!Native.IsWindows) return;
            pct = Math.Max(0, Math.Min(100, pct));
            bool done = false;
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(new System.Management.ManagementScope(@"root\WMI"), new System.Management.SelectQuery("WmiMonitorBrightnessMethods")))
                    foreach (System.Management.ManagementObject o in s.Get()) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)pct }); done = true; }
            }
            catch { }
            if (done) return;
            Each(h => { uint mn, cur, mx; if (GetMonitorBrightness(h, out mn, out cur, out mx) && mx > mn) SetMonitorBrightness(h, (uint)(mn + (mx - mn) * pct / 100)); });
        }
        static void Each(Action<IntPtr> a)
        {
            foreach (var sc in Screen.AllScreens)
            {
                try
                {
                    var c = sc.Bounds; var mon = MonitorFromPoint(new POINT { x = c.X + c.Width / 2, y = c.Y + c.Height / 2 }, 2);
                    uint n; if (!GetNumberOfPhysicalMonitorsFromHMONITOR(mon, out n) || n == 0) continue;
                    var arr = new PHYSICAL_MONITOR[n];
                    if (!GetPhysicalMonitorsFromHMONITOR(mon, n, arr)) continue;
                    try { foreach (var pm in arr) a(pm.h); } finally { DestroyPhysicalMonitors(n, arr); }
                }
                catch { }
            }
        }
    }

    // ======================================================================== Windows Runtime through reflection
    internal static class WinRt
    {
        static MethodInfo asTask;
        public static Type T(string name, string winmd) { return Type.GetType(name + ", " + winmd + ", ContentType=WindowsRuntime", true); }
        static void Load()
        {
            if (asTask != null) return;
            var rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            var ms = rt.GetType("System.WindowsRuntimeSystemExtensions", true).GetMethods();
            asTask = ms.First(m => m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
        }
        public static object Await(object op, Type result)
        {
            Load();
            var task = (Task)asTask.MakeGenericMethod(result).Invoke(null, new[] { op });
            if (!task.Wait(6000)) return null;
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }
    }

    /// <summary>Wi-Fi and Bluetooth on/off, the same switches as in Windows Settings.</summary>
    internal static class Radios
    {
        static Type tRadio, tState, tAccess;
        static bool asked;
        /// <summary>RadioKind: 1 = Wi-Fi, 3 = Bluetooth. Returns null when Windows can't tell.</summary>
        static List<object> All()
        {
            if (tRadio == null)
            {
                tRadio = WinRt.T("Windows.Devices.Radios.Radio", "Windows.Devices.Radios");
                tState = WinRt.T("Windows.Devices.Radios.RadioState", "Windows.Devices.Radios");
                tAccess = WinRt.T("Windows.Devices.Radios.RadioAccessStatus", "Windows.Devices.Radios");
            }
            if (!asked) { asked = true; WinRt.Await(tRadio.GetMethod("RequestAccessAsync").Invoke(null, null), tAccess); }
            var listType = typeof(IReadOnlyList<>).MakeGenericType(tRadio);
            var list = WinRt.Await(tRadio.GetMethod("GetRadiosAsync").Invoke(null, null), listType) as IEnumerable;
            var l = new List<object>(); if (list != null) foreach (var r in list) l.Add(r);
            return l;
        }
        static int Kind(object r) { return Convert.ToInt32(tRadio.GetProperty("Kind").GetValue(r, null)); }
        static int State(object r) { return Convert.ToInt32(tRadio.GetProperty("State").GetValue(r, null)); }

        /// <summary>1 = on, 0 = off, -1 = this PC has no such radio (or Windows can't tell).</summary>
        public static void Read(out int wifi, out int bt)
        {
            wifi = bt = -1;
            if (!Native.IsWindows) return;
            foreach (var r in All())
            {
                int k = Kind(r), s = State(r);
                int v = s == 1 ? 1 : s == 2 || s == 3 ? 0 : -1;
                if (k == 1 && wifi != 1) wifi = v;
                if (k == 3 && bt != 1) bt = v;
            }
        }
        /// <summary>Turns that radio on or off. False when it didn't work (then Settings is opened instead).</summary>
        public static bool Set(int kind, bool on)
        {
            bool any = false;
            foreach (var r in All())
            {
                if (Kind(r) != kind) continue;
                var op = tRadio.GetMethod("SetStateAsync").Invoke(r, new[] { Enum.ToObject(tState, on ? 1 : 2) });
                object res = WinRt.Await(op, tAccess);
                if (res != null && Convert.ToInt32(res) == 1) any = true;   // Allowed
            }
            return any;
        }
    }

    internal static class DarkMode
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        public static bool IsDark()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0; }
            catch { return false; }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, int flags, int timeout, out IntPtr result);
        /// <summary>Switches Windows (apps and taskbar) to dark or light, like Settings › Personalization › Colors.</summary>
        public static void Set(bool dark)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    k.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
                    k.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
                }
                IntPtr r; SendMessageTimeout(new IntPtr(0xffff), 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 300, out r);
            }
            catch { }
        }
    }

    /// <summary>One background thread for slow system calls, so the desktop never waits for them.</summary>
    internal sealed class Worker
    {
        readonly BlockingCollection<Action> jobs = new BlockingCollection<Action>();
        public Worker(string name)
        {
            var t = new Thread(delegate () { foreach (var j in jobs.GetConsumingEnumerable()) try { j(); } catch (Exception ex) { Program.Log(ex); } }) { IsBackground = true, Name = name };
            t.Start();
        }
        public void Do(Action a) { if (jobs.Count < 20) jobs.Add(a); }
    }

    // ======================================================================== the widget
    internal class QuickWidget : Widget
    {
        float volume = -1; bool muted; int bright = -2;   // -2 = not read yet, -1 = not supported
        int wifi = -1, bt = -1; bool dark;
        bool sample;
        System.Windows.Forms.Timer timer;
        readonly Worker work = new Worker("GlassDesk quick settings");
        int pendingBright = -1; bool brightBusy;
        string dragging;                       // "vol" or "bright" while a slider is being dragged
        RectangleF[] tiles = new RectangleF[4];
        RectangleF volBar, brightBar;
        int hot = -1;
        DateTime ignoreRadiosUntil;

        protected override Color MacColor { get { return Color.FromArgb(255, 10, 132, 255); } }
        public QuickWidget(App app) : base(app, "quick") { }
        protected override Size BaseSize { get { return new Size(330, 228); } }

        public void SetSample() { sample = true; volume = 0.62f; bright = 75; wifi = 1; bt = 0; dark = true; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            timer = new System.Windows.Forms.Timer { Interval = 1500 };
            int n = 0;
            timer.Tick += delegate { if (Visible && !App.Paused && dragging == null) { Poll(++n % 6 == 0); } };
            timer.Start();
            Poll(true);
        }
        protected override void Dispose(bool disposing) { if (disposing && timer != null) timer.Dispose(); base.Dispose(disposing); }

        void Poll(bool slow)
        {
            if (sample || App.Preview || !Native.IsWindows) return;
            bool m; float v = Sound.Get(out m);
            bool d = DarkMode.IsDark();
            bool changed = Math.Abs(v - volume) > 0.004f || m != muted || d != dark;
            volume = v; muted = m; dark = d;
            if (changed) Redraw();
            if (slow || bright == -2)
                work.Do(delegate
                {
                    int b = brightBusy ? bright : Brightness.Get();
                    int w, t; try { Radios.Read(out w, out t); } catch { w = t = -1; }
                    Ui(delegate { if (pendingBright < 0) bright = b; if (DateTime.Now > ignoreRadiosUntil) { wifi = w; bt = t; } Redraw(); });
                });
        }
        void Ui(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        // ---------------- drawing ----------------
        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, Lang.L("Quick settings"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            float top = r.Y + T.Sf(30), cw = r.Width / 4f, d = Math.Min(T.Sf(48), cw - T.Sf(14));
            string[] names = { Lang.L("Wi-Fi"), Lang.L("Bluetooth"), Lang.L("Dark mode"), muted ? Lang.L("Muted") : Lang.L("Sound") };
            bool[] on = { wifi == 1, bt == 1, dark, !muted && volume > 0 };
            bool[] na = { wifi < 0 && !sample, bt < 0 && !sample, false, volume < 0 };
            for (int i = 0; i < 4; i++)
            {
                var c = new RectangleF(r.X + cw * i + (cw - d) / 2, top, d, d);
                tiles[i] = RectangleF.Inflate(c, T.Sf(4), T.Sf(4));
                Color fill = on[i] ? T.Accent : Color.FromArgb(T.Dark ? 46 : 30, T.Dark ? Color.White : Color.Black);
                using (var b = new SolidBrush(fill)) g.FillEllipse(b, c);
                if (hot == i) using (var b = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillEllipse(b, c);
                Color ic = on[i] ? Color.White : (na[i] ? T.SubText : T.Text);
                DrawIcon(g, i, c, ic, fill);
                Txt(g, names[i], Gfx.F(T.FontName, T.Sf(10.5f), FontStyle.Regular), na[i] ? T.SubText : T.Text, new RectangleF(c.X - T.Sf(14), c.Bottom + T.Sf(3), d + T.Sf(28), T.Sf(18)), Gfx.Center);
            }
            float sy = top + d + T.Sf(32);
            volBar = Slider(g, new RectangleF(r.X, sy, r.Width, T.Sf(30)), 0, volume < 0 ? 0 : (muted ? 0 : volume), volume < 0 ? Lang.L("No sound device") : null);
            brightBar = Slider(g, new RectangleF(r.X, sy + T.Sf(40), r.Width, T.Sf(30)), 1, bright < 0 ? 0 : bright / 100f,
                bright == -1 ? Lang.L("This screen can't be dimmed from here") : bright == -2 ? "…" : null);
        }

        RectangleF Slider(Graphics g, RectangleF row, int kind, float v, string off)
        {
            float isz = T.Sf(20);
            var ir = new RectangleF(row.X, row.Y + (row.Height - isz) / 2, isz, isz);
            if (kind == 0) Speaker(g, ir, T.Text, muted); else Sun(g, ir, T.Text);
            var bar = new RectangleF(ir.Right + T.Sf(12), row.Y + row.Height / 2 - T.Sf(4), row.Width - isz - T.Sf(62), T.Sf(8));
            if (off != null)
            {
                Txt(g, off, Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(bar.X, row.Y, row.Width - isz - T.Sf(12), row.Height), Gfx.Left);
                return RectangleF.Empty;
            }
            Bar(g, bar, v, T.Accent);
            float kx = bar.X + bar.Width * Math.Max(0, Math.Min(1, v)), kd = T.Sf(18);
            var knob = new RectangleF(kx - kd / 2, bar.Y + bar.Height / 2 - kd / 2, kd, kd);
            using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(b, knob.X, knob.Y + T.Sf(1), kd, kd);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, knob);
            Txt(g, ((int)Math.Round(v * 100)) + "%", Gfx.F(T.FontName, T.Sf(11.5f), FontStyle.Bold), T.SubText, new RectangleF(bar.Right + T.Sf(8), row.Y, T.Sf(42), row.Height), Gfx.Right);
            return RectangleF.Inflate(bar, T.Sf(8), T.Sf(10));
        }

        void DrawIcon(Graphics g, int i, RectangleF c, Color col, Color bg)
        {
            float s = c.Width * 0.46f; var m = new PointF(c.X + c.Width / 2, c.Y + c.Height / 2);
            var ir = new RectangleF(m.X - s / 2, m.Y - s / 2, s, s);
            if (i == 0) Wifi(g, ir, col);
            else if (i == 1) Bt(g, ir, col);
            else if (i == 2) Moon(g, ir, col, bg);
            else Speaker(g, ir, col, muted);
        }
        Pen P(Color c, float w) { return new Pen(c, T.Sf(w)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; }
        void Wifi(Graphics g, RectangleF r, Color c)
        {
            var bottom = new PointF(r.X + r.Width / 2, r.Bottom - r.Height * 0.08f);
            using (var pen = P(c, 2f))
                for (int k = 1; k <= 3; k++)
                {
                    float rad = r.Width * 0.2f * k * 0.95f;
                    g.DrawArc(pen, bottom.X - rad, bottom.Y - rad, rad * 2, rad * 2, 225, 90);
                }
            using (var b = new SolidBrush(c)) g.FillEllipse(b, bottom.X - T.Sf(2), bottom.Y - T.Sf(3), T.Sf(4), T.Sf(4));
        }
        void Bt(Graphics g, RectangleF r, Color c)
        {
            float x = r.X + r.Width / 2, w = r.Width * 0.3f;
            using (var pen = P(c, 1.8f))
                g.DrawLines(pen, new[] { new PointF(x - w, r.Y + r.Height * 0.28f), new PointF(x + w, r.Y + r.Height * 0.7f), new PointF(x, r.Bottom), new PointF(x, r.Y),
                    new PointF(x + w, r.Y + r.Height * 0.3f), new PointF(x - w, r.Y + r.Height * 0.72f) });
        }
        void Moon(Graphics g, RectangleF r, Color c, Color bg)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
            using (var b = new SolidBrush(bg)) g.FillEllipse(b, r.X + r.Width * 0.38f, r.Y - r.Height * 0.12f, r.Width * 0.82f, r.Height * 0.82f);
        }
        void Speaker(Graphics g, RectangleF r, Color c, bool mute)
        {
            using (var b = new SolidBrush(c))
                g.FillPolygon(b, new[] { new PointF(r.X, r.Y + r.Height * 0.36f), new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.36f), new PointF(r.X + r.Width * 0.5f, r.Y + r.Height * 0.1f),
                    new PointF(r.X + r.Width * 0.5f, r.Bottom - r.Height * 0.1f), new PointF(r.X + r.Width * 0.22f, r.Bottom - r.Height * 0.36f), new PointF(r.X, r.Bottom - r.Height * 0.36f) });
            using (var pen = P(c, 1.7f))
            {
                if (mute)
                {
                    g.DrawLine(pen, r.X + r.Width * 0.66f, r.Y + r.Height * 0.34f, r.Right - r.Width * 0.02f, r.Bottom - r.Height * 0.34f);
                    g.DrawLine(pen, r.X + r.Width * 0.66f, r.Bottom - r.Height * 0.34f, r.Right - r.Width * 0.02f, r.Y + r.Height * 0.34f);
                }
                else
                {
                    g.DrawArc(pen, r.X + r.Width * 0.3f, r.Y + r.Height * 0.28f, r.Width * 0.44f, r.Height * 0.44f, -50, 100);
                    g.DrawArc(pen, r.X + r.Width * 0.18f, r.Y + r.Height * 0.08f, r.Width * 0.8f, r.Height * 0.84f, -50, 100);
                }
            }
        }
        void Sun(Graphics g, RectangleF r, Color c)
        {
            var m = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2); float cr = r.Width * 0.22f;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, m.X - cr, m.Y - cr, cr * 2, cr * 2);
            using (var pen = P(c, 1.6f))
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4; float r1 = r.Width * 0.36f, r2 = r.Width * 0.5f;
                    g.DrawLine(pen, m.X + (float)Math.Cos(a) * r1, m.Y + (float)Math.Sin(a) * r1, m.X + (float)Math.Cos(a) * r2, m.Y + (float)Math.Sin(a) * r2);
                }
        }

        // ---------------- input ----------------
        protected override bool IsDragArea(Point p)
        {
            if (volBar.Contains(p) || brightBar.Contains(p)) return false;
            foreach (var t in tiles) if (t.Contains(p)) return false;
            return true;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !IsRolled)
            {
                if (!volBar.IsEmpty && volBar.Contains(e.Location)) { dragging = "vol"; Capture = true; SlideTo(e.X); return; }
                if (!brightBar.IsEmpty && brightBar.Contains(e.Location)) { dragging = "bright"; Capture = true; SlideTo(e.X); return; }
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging != null) { SlideTo(e.X); return; }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging != null) { SlideTo(e.X); dragging = null; Capture = false; return; }
            base.OnMouseUp(e);
        }
        protected override void OnHover(Point p)
        {
            int h = -1; for (int i = 0; i < 4; i++) if (tiles[i].Contains(p)) h = i;
            if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Redraw(); }
            else if (volBar.Contains(p) || brightBar.Contains(p)) Cursor = Cursors.Hand;
        }
        protected override void OnWheel(int delta)
        {
            if (volume < 0) return;
            volume = Math.Max(0, Math.Min(1, volume + (delta > 0 ? 0.02f : -0.02f)));
            if (!sample) Sound.Set(volume);
            muted = false; Redraw();
        }

        void SlideTo(int x)
        {
            var bar = dragging == "vol" ? volBar : brightBar;
            var inner = RectangleF.Inflate(bar, -T.Sf(8), 0);
            float v = Math.Max(0, Math.Min(1, (x - inner.X) / Math.Max(1, inner.Width)));
            if (dragging == "vol") { volume = v; muted = false; if (!sample) Sound.Set(v); }
            else { bright = (int)Math.Round(v * 100); QueueBright(bright); }
            Redraw();
        }
        /// <summary>Monitors take a moment to change brightness, so only the newest value is sent.</summary>
        void QueueBright(int v)
        {
            if (sample) return;
            pendingBright = v;
            if (brightBusy) return;
            brightBusy = true;
            work.Do(delegate
            {
                while (true)
                {
                    int want = pendingBright; if (want < 0) break;
                    Brightness.Set(want);
                    if (pendingBright == want) { pendingBright = -1; break; }
                }
                brightBusy = false;
            });
        }

        protected override void OnClickAt(Point p, MouseButtons b)
        {
            if (b == MouseButtons.Right) { base.OnClickAt(p, b); return; }
            for (int i = 0; i < 4; i++)
            {
                if (!tiles[i].Contains(p)) continue;
                if (i == 3) { if (volume < 0) return; muted = !muted; if (!sample) Sound.SetMute(muted); Redraw(); return; }
                if (i == 2) { dark = !dark; Redraw(); bool want = dark; if (!sample) work.Do(delegate { DarkMode.Set(want); }); return; }
                // Wi-Fi / Bluetooth: flip it straight away on screen, then ask Windows
                int kind = i == 0 ? 1 : 3; int cur = i == 0 ? wifi : bt;
                if (sample) { if (i == 0) wifi = wifi == 1 ? 0 : 1; else bt = bt == 1 ? 0 : 1; Redraw(); return; }
                if (cur < 0) { OpenSettings(i == 0 ? "ms-settings:network-wifi" : "ms-settings:bluetooth"); return; }
                bool turnOn = cur != 1;
                if (i == 0) wifi = turnOn ? 1 : 0; else bt = turnOn ? 1 : 0;
                ignoreRadiosUntil = DateTime.Now.AddSeconds(4);
                Redraw();
                work.Do(delegate
                {
                    bool ok = false; try { ok = Radios.Set(kind, turnOn); } catch { }
                    if (!ok) Ui(delegate { if (i == 0) wifi = cur; else bt = cur; Redraw(); OpenSettings(i == 0 ? "ms-settings:network-wifi" : "ms-settings:bluetooth"); });
                });
                return;
            }
        }
        static void OpenSettings(string uri) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); } catch { } }

        protected override string RolledHint() { return volume < 0 ? "" : (muted ? Lang.L("Muted") : (int)Math.Round(volume * 100) + "%"); }
    }
}
