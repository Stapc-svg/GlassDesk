// GlassDesk 2.2 - Game stats widget: graphics card load, its memory and temperature, CPU and PC temperature.
// GPU load and memory come from the same Windows counters Task Manager uses (any NVIDIA, AMD or Intel GPU).
// GPU temperature and fan speed come from the NVIDIA driver when there is one (nvml.dll); Windows has no
// common way for other cards. The PC temperature is what the motherboard reports to Windows.
// Read on a background thread every 2 seconds, only while the widget is on screen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace GlassDesk
{
    internal sealed class GpuReader
    {
        // ---- Windows performance counters (PDH) ----
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
        const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000;

        // ---- NVIDIA driver ----
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
        [DllImport("nvml.dll")] static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr dev);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensor, out uint temp);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetFanSpeed(IntPtr dev, out uint speed);

        IntPtr query, cEngine, cMem, cTherm;
        IntPtr nvDev; bool nvTried;
        public string Name = "";
        public long VramTotal;
        // latest values (-1 = unknown)
        public volatile float Load = -1, GpuTemp = -1, Fan = -1, PcTemp = -1;
        public long VramUsed = -1;

        public GpuReader()
        {
            ReadAdapter();
            if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
            PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out cEngine);
            PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out cMem);
            PdhAddEnglishCounter(query, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out cTherm);
            PdhCollectQueryData(query);   // the first reading only sets the starting point
        }

        /// <summary>The graphics card's name and memory size, from the driver's registry entry.</summary>
        void ReadAdapter()
        {
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;
                        using (var k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            long mem = 0;
                            object q = k.GetValue("HardwareInformation.qwMemorySize");
                            if (q is long) mem = (long)q;
                            else { object d = k.GetValue("HardwareInformation.MemorySize"); if (d is int) mem = (uint)(int)d; else if (d is byte[] && ((byte[])d).Length >= 4) mem = BitConverter.ToUInt32((byte[])d, 0); }
                            string desc = k.GetValue("DriverDesc") as string ?? "";
                            if (mem > VramTotal) { VramTotal = mem; Name = desc; }
                            else if (Name.Length == 0) Name = desc;
                        }
                    }
                }
            }
            catch { }
            Name = Name.Replace("(R)", "").Replace("(TM)", "").Trim();
        }

        Dictionary<string, double> Array(IntPtr counter)
        {
            var res = new Dictionary<string, double>();
            if (counter == IntPtr.Zero) return res;
            uint size = 0, count;
            int st = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, IntPtr.Zero);
            if (size == 0) return res;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf) != 0) return res;
                // PDH_FMT_COUNTERVALUE_ITEM: name pointer, then { status, (padding), double }: 24 bytes on 32- and 64-bit
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buf + i * 24;
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                    int status = Marshal.ReadInt32(item + 8);
                    if (status != 0 && status != 1) continue;   // PDH_CSTATUS_VALID_DATA / NEW_DATA
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + 16));
                    res[name] = v;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return res;
        }

        public void Sample()
        {
            if (query != IntPtr.Zero && PdhCollectQueryData(query) == 0)
            {
                // like Task Manager: add up every app on each engine of the card, show the busiest engine
                var engines = new Dictionary<string, double>();
                foreach (var kv in Array(cEngine))
                {
                    int at = kv.Key.IndexOf("luid_", StringComparison.Ordinal);
                    string eng = at >= 0 ? kv.Key.Substring(at) : kv.Key;
                    double cur; engines.TryGetValue(eng, out cur); engines[eng] = cur + kv.Value;
                }
                double best = engines.Count > 0 ? 0 : -1; foreach (var v in engines.Values) best = Math.Max(best, v);
                Load = best < 0 ? -1 : (float)Math.Min(100, best);
                long used = -1; foreach (var v in Array(cMem).Values) used = Math.Max(used, (long)v);
                VramUsed = used;
                double k = -1; foreach (var v in Array(cTherm).Values) k = Math.Max(k, v);
                PcTemp = k > 250 ? (float)(k - 273.15) : -1;   // Kelvin
            }
            Nvidia();
        }

        void Nvidia()
        {
            try
            {
                if (!nvTried)
                {
                    nvTried = true;
                    string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll");
                    string sys = Path.Combine(Environment.SystemDirectory, "nvml.dll");
                    if (File.Exists(sys)) LoadLibrary(sys); else if (File.Exists(old)) LoadLibrary(old); else return;
                    if (nvmlInit_v2() != 0 || nvmlDeviceGetHandleByIndex_v2(0, out nvDev) != 0) nvDev = IntPtr.Zero;
                }
                if (nvDev == IntPtr.Zero) return;
                uint t, f;
                GpuTemp = nvmlDeviceGetTemperature(nvDev, 0, out t) == 0 ? (float)t : -1f;
                Fan = nvmlDeviceGetFanSpeed(nvDev, out f) == 0 ? (float)f : -1f;
            }
            catch { nvDev = IntPtr.Zero; }
        }
    }

    internal class GameWidget : Widget
    {
        GpuReader gpu;
        readonly object gate = new object();
        System.Threading.Timer timer;
        bool sample;
        float sLoad, sTemp, sFan, sPc; long sUsed, sTotal; string sName = "";
        readonly float[] hist = new float[40];

        protected override Color MacColor { get { return Color.FromArgb(255, 118, 214, 75); } }
        public GameWidget(App app) : base(app, "game") { }
        protected override Size BaseSize { get { return new Size(340, 196); } }

        public void SetSample()
        {
            sample = true; sLoad = 87; sTemp = 68; sFan = 54; sPc = 47; sUsed = (long)(6.1 * 1073741824); sTotal = 8L * 1073741824; sName = "NVIDIA GeForce RTX 4060";
            for (int i = 0; i < hist.Length; i++) hist[i] = (float)(0.62 + 0.18 * Math.Sin(i * 0.5) + 0.07 * Math.Sin(i * 1.9));
            hist[hist.Length - 1] = 0.87f;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (sample || App.Preview || !Native.IsWindows) return;
            if (Visible && timer == null) timer = new System.Threading.Timer(Tick, null, 300, 2000);
            else if (!Visible && timer != null) { timer.Dispose(); timer = null; }
        }
        protected override void Dispose(bool disposing) { if (disposing && timer != null) { timer.Dispose(); timer = null; } base.Dispose(disposing); }

        void Tick(object _)
        {
            if (App.Paused) return;
            if (!Monitor.TryEnter(gate)) return;   // the last reading is still going
            try
            {
                if (gpu == null) gpu = new GpuReader();
                gpu.Sample();
                float l = gpu.Load; long u = gpu.VramUsed;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        sLoad = l; sUsed = u; sTotal = gpu.VramTotal; sTemp = gpu.GpuTemp; sFan = gpu.Fan; sPc = gpu.PcTemp; sName = gpu.Name;
                        System.Array.Copy(hist, 1, hist, 0, hist.Length - 1); hist[hist.Length - 1] = Math.Max(0, l) / 100f;
                        Redraw();
                    }));
                }
                catch { }
            }
            catch (Exception ex) { Program.Log(ex); }
            finally { Monitor.Exit(gate); }
        }

        static string Gb(long b) { return (b / 1073741824.0).ToString(b < 10L * 1073741824 ? "0.0" : "0", CultureInfo.InvariantCulture); }

        protected override void PaintContent(Graphics g)
        {
            Rectangle r = Inner;
            Title(g, Lang.L("Game stats"), new Rectangle(r.X, r.Y, r.Width, T.S(20)));
            Txt(g, sName.Length > 0 ? sName : Lang.L("Graphics card"), Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular), T.SubText, new RectangleF(r.X + T.Sf(110), r.Y, r.Width - T.Sf(110), T.Sf(20)), Gfx.Right);
            var st = App.Stats;
            float cpu = st != null ? st.Cpu : 0;
            var items = new List<Tuple<string, string, float, Color>>
            {
                Tuple.Create(Lang.L("GPU"), sLoad < 0 ? "—" : sLoad.ToString("0") + "%", Math.Max(0, sLoad) / 100f, Color.FromArgb(255, 118, 214, 75)),
                Tuple.Create(Lang.L("GPU memory"), sUsed < 0 ? "—" : Gb(sUsed) + (sTotal > 0 ? "/" + Gb(sTotal) : "") + " GB", sTotal > 0 && sUsed > 0 ? (float)sUsed / sTotal : 0, Color.FromArgb(255, 10, 132, 255)),
                Tuple.Create(Lang.L("GPU temp"), sTemp < 0 ? "—" : sTemp.ToString("0") + "°", sTemp < 0 ? 0 : sTemp / 100f, Heat(sTemp)),
                Tuple.Create(Lang.L("CPU"), (cpu * 100).ToString("0") + "%", cpu, Color.FromArgb(255, 255, 159, 10)),
            };
            float cw = r.Width / 4f, d0 = Math.Min(T.Sf(60), cw - T.Sf(12)), top = r.Y + T.Sf(32);
            for (int i = 0; i < items.Count; i++)
            {
                float cx = r.X + cw * i + cw / 2;
                var ring = new RectangleF(cx - d0 / 2, top, d0, d0);
                float pw = T.Sf(5.5f);
                var inner = RectangleF.Inflate(ring, -pw / 2, -pw / 2);
                var it = items[i];
                using (var pen = new Pen(Color.FromArgb(T.Dark ? 50 : 34, it.Item4), pw)) g.DrawEllipse(pen, inner);
                float sweep = 360 * Math.Max(0, Math.Min(1, it.Item3));
                if (sweep > 0.5f) using (var pen = new Pen(it.Item4, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, inner, -90, sweep);
                string v = it.Item2; bool small = v.Length > 5;
                Txt(g, small ? v.Replace(" GB", "") : v, Gfx.F(T.FontName, T.Sf(small ? 10.5f : 13), FontStyle.Bold), T.Text, ring, Gfx.Center);
                Txt(g, it.Item1, Gfx.F(T.FontName, T.Sf(10.5f), FontStyle.Regular), T.SubText, new RectangleF(cx - cw / 2, ring.Bottom + T.Sf(4), cw, T.Sf(18)), Gfx.Center);
            }
            // GPU load over the last 80 seconds, plus fan and PC temperature
            var sp = new RectangleF(r.X, top + d0 + T.Sf(30), r.Width * 0.55f, T.Sf(30));
            var pts = new PointF[hist.Length];
            for (int i = 0; i < hist.Length; i++) pts[i] = new PointF(sp.X + sp.Width * i / (hist.Length - 1), sp.Bottom - 1 - (sp.Height - 2) * Math.Max(0, Math.Min(1, hist[i])));
            Color c = Color.FromArgb(255, 118, 214, 75);
            using (var path = new GraphicsPath())
            {
                path.AddLines(pts); path.AddLine(sp.Right, sp.Bottom, sp.X, sp.Bottom); path.CloseFigure();
                using (var lg = new LinearGradientBrush(new PointF(0, sp.Y - 1), new PointF(0, sp.Bottom + 1), Gfx.WithAlpha(c, 100), Gfx.WithAlpha(c, 0))) g.FillPath(lg, path);
            }
            using (var pen = new Pen(c, T.Sf(1.5f)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
            var f = Gfx.F(T.FontName, T.Sf(11), FontStyle.Regular);
            var info = new RectangleF(sp.Right + T.Sf(14), sp.Y - T.Sf(4), r.Right - sp.Right - T.Sf(14), T.Sf(19));
            Txt(g, Lang.L("Fan") + "  " + (sFan < 0 ? "—" : sFan.ToString("0") + "%"), f, T.SubText, info, Gfx.Right);
            info.Y += T.Sf(19);
            Txt(g, Lang.L("PC temp") + "  " + (sPc < 0 ? "—" : sPc.ToString("0") + "°C"), f, T.SubText, info, Gfx.Right);
        }
        static Color Heat(float t)
        {
            if (t >= 83) return Color.FromArgb(255, 255, 69, 58);
            if (t >= 72) return Color.FromArgb(255, 255, 159, 10);
            return Color.FromArgb(255, 100, 210, 255);
        }
        protected override string RolledHint() { return sLoad < 0 ? "" : "GPU " + sLoad.ToString("0") + "%" + (sTemp >= 0 ? " · " + sTemp.ToString("0") + "°" : ""); }
    }
}
