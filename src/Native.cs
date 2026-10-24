// GlassDesk - Native Windows API calls (kept small; everything is optional and guarded)
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace GlassDesk
{
    internal static class Native
    {
        public static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        public static readonly int WinBuild = IsWindows ? Environment.OSVersion.Version.Build : 0;

        // ---------- Layered windows ----------
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; public POINT(int x, int y) { this.x = x; this.y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        // ---------- Blur behind (Windows 10 / 11) ----------
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }
        [StructLayout(LayoutKind.Sequential)] struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);

        public static void SetBlur(IntPtr hwnd, bool on)
        {
            if (!IsWindows || WinBuild < 10240) return;
            try
            {
                var accent = new AccentPolicy { AccentState = on ? 3 : 0 }; // 3 = ACCENT_ENABLE_BLURBEHIND
                int size = Marshal.SizeOf(accent);
                IntPtr p = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, p, false);
                    var data = new WinCompAttrData { Attribute = 19, Data = p, SizeOfData = size };
                    SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (!IsWindows || WinBuild < 17763) return;
            try { int v = dark ? 1 : 0; if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, 4); } catch { }
        }
        public static void SetRoundCorners(IntPtr hwnd)
        {
            if (!IsWindows || WinBuild < 22000) return;
            try { int v = 2; DwmSetWindowAttribute(hwnd, 33, ref v, 4); } catch { }
        }

        // ---------- Window placement ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr h, int idx, int v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);
        public static void SetOwner(IntPtr h, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, -8, owner); else SetWindowLong32(h, -8, owner.ToInt32());
        }
        public static IntPtr DesktopOwner()
        {
            if (!IsWindows) return IntPtr.Zero;
            try { return FindWindow("Progman", null); } catch { return IntPtr.Zero; }
        }

        // Windows desktop icons (the SysListView32 inside SHELLDLL_DefView)
        static IntPtr DesktopListView(out IntPtr defView)
        {
            defView = IntPtr.Zero;
            IntPtr progman = FindWindow("Progman", null);
            defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr worker = IntPtr.Zero;
            while (defView == IntPtr.Zero)
            {
                worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                if (worker == IntPtr.Zero) break;
                defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            }
            if (defView == IntPtr.Zero) return IntPtr.Zero;
            return FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }
        public static void SetDesktopIconsVisible(bool visible)
        {
            if (!IsWindows) return;
            try
            {
                IntPtr defView;
                IntPtr lv = DesktopListView(out defView);
                if (lv == IntPtr.Zero) return;
                if (IsWindowVisible(lv) != visible) SendMessage(defView, 0x111, new IntPtr(0x7402), IntPtr.Zero); // toggle
            }
            catch { }
        }

        // ---------- System info (very cheap calls) ----------
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        public static bool Memory(out ulong total, out ulong avail)
        {
            total = avail = 0;
            if (!IsWindows) return false;
            try
            {
                var m = new MEMORYSTATUSEX(); m.dwLength = (uint)Marshal.SizeOf(m);
                if (!GlobalMemoryStatusEx(ref m)) return false;
                total = m.ullTotalPhys; avail = m.ullAvailPhys; return true;
            }
            catch { return false; }
        }

        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        /// <summary>True when a full-screen game / presentation / video is running.</summary>
        public static bool FullScreenAppRunning()
        {
            if (!IsWindows) return false;
            try { int s; if (SHQueryUserNotificationState(out s) == 0) return s == 2 || s == 3 || s == 4; } catch { }
            return false;
        }

        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        public static void TrimMemory()
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); if (IsWindows) SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
        }

        // ---------- Hotkey ----------
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);

        // ---------- DPI ----------
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int v);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        public static void MakeDpiAware()
        {
            if (!IsWindows) return;
            try { if (SetProcessDpiAwareness(1) == 0) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        // ---------- Shell icons ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImageList
        {
            [PreserveSig] int Add(IntPtr a, IntPtr b, ref int c);
            [PreserveSig] int ReplaceIcon(int i, IntPtr h, ref int c);
            [PreserveSig] int SetOverlayImage(int a, int b);
            [PreserveSig] int Replace(int i, IntPtr a, IntPtr b);
            [PreserveSig] int AddMasked(IntPtr a, int b, ref int c);
            [PreserveSig] int Draw(IntPtr p);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr icon);
        }
        [DllImport("shell32.dll", EntryPoint = "#727")]
        static extern int SHGetImageList(int list, ref Guid riid, out IImageList ppv);

        /// <summary>Returns the shell icon of a file/folder as a 48px bitmap (falls back to 32px).</summary>
        public static Bitmap GetShellIcon(string path)
        {
            if (!IsWindows) return null;
            try
            {
                const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;
                var info = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_LARGEICON);
                IntPtr h = info.hIcon;
                if (h == IntPtr.Zero) return null;
                Bitmap bmp;
                using (var ic = Icon.FromHandle(h)) bmp = ic.ToBitmap();
                DestroyIcon(h);
                return bmp;
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd; public uint wFunc; public string pFrom, pTo; public ushort fFlags;
            public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        /// <summary>Moves a file or folder to the Recycle Bin.</summary>
        public static bool RecycleFile(string path)
        {
            try
            {
                var op = new SHFILEOPSTRUCT { wFunc = 3 /*FO_DELETE*/, pFrom = path + "\0\0", fFlags = 0x40 | 0x10 | 0x4 /*ALLOWUNDO|NOCONFIRMATION|SILENT*/ };
                return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
            }
            catch { return false; }
        }

        /// <summary>Moves or copies files with the normal Windows progress / conflict UI (undo-able with Ctrl+Z in Explorer).</summary>
        public static bool ShellTransfer(IntPtr owner, string[] paths, string destDir, bool copy)
        {
            try
            {
                var from = new System.Text.StringBuilder();
                foreach (string p in paths) from.Append(p).Append('\0');
                from.Append('\0');
                var op = new SHFILEOPSTRUCT
                {
                    hwnd = owner, wFunc = copy ? 2u : 1u /*FO_COPY : FO_MOVE*/, pFrom = from.ToString(), pTo = destDir + "\0\0",
                    fFlags = 0x40 | 0x8 | 0x200 /*ALLOWUNDO | RENAMEONCOLLISION | NOCONFIRMMKDIR*/
                };
                return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHSTOCKICONINFO
        {
            public uint cbSize; public IntPtr hIcon; public int iSysIconIndex, iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
        }
        [DllImport("shell32.dll")] static extern int SHGetStockIconInfo(uint siid, uint flags, ref SHSTOCKICONINFO info);
        /// <summary>A built-in Windows icon (31 = empty Recycle Bin, 32 = full Recycle Bin).</summary>
        public static Bitmap GetStockIcon(uint id)
        {
            if (!IsWindows) return null;
            try
            {
                var info = new SHSTOCKICONINFO(); info.cbSize = (uint)Marshal.SizeOf(info);
                if (SHGetStockIconInfo(id, 0x100 /*SHGSI_ICON*/, ref info) != 0 || info.hIcon == IntPtr.Zero) return null;
                Bitmap bmp;
                using (var ic = Icon.FromHandle(info.hIcon)) bmp = ic.ToBitmap();
                DestroyIcon(info.hIcon);
                return bmp;
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential)] struct SHQUERYRBINFO64 { public int cbSize; public long i64Size, i64NumItems; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct SHQUERYRBINFO32 { public int cbSize; public long i64Size, i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")] static extern int SHQueryRecycleBin64(string root, ref SHQUERYRBINFO64 info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")] static extern int SHQueryRecycleBin32(string root, ref SHQUERYRBINFO32 info);
        public static bool RecycleBinFull()
        {
            if (!IsWindows) return false;
            try
            {
                if (IntPtr.Size == 8)
                {
                    var q = new SHQUERYRBINFO64(); q.cbSize = Marshal.SizeOf(q);
                    return SHQueryRecycleBin64(null, ref q) == 0 && q.i64NumItems > 0;
                }
                var q32 = new SHQUERYRBINFO32(); q32.cbSize = Marshal.SizeOf(q32);
                return SHQueryRecycleBin32(null, ref q32) == 0 && q32.i64NumItems > 0;
            }
            catch { return false; }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        public static void EmptyRecycleBin(IntPtr owner) { try { SHEmptyRecycleBin(owner, null, 0); } catch { } }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool SHObjectProperties(IntPtr hwnd, int type, string obj, string page);
        public static void ShowProperties(string path)
        {
            try { SHObjectProperties(IntPtr.Zero, 2, path, null); } catch { }
        }

        public static bool AppsUseLightTheme()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }
    }
}
