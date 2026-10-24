// GlassDesk - entry point
using System;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("GlassDesk")]
[assembly: System.Reflection.AssemblyProduct("GlassDesk")]
[assembly: System.Reflection.AssemblyDescription("Glass desktop organizer with widgets")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]

namespace GlassDesk
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--preview") return Preview(args[1], args.Length > 2 && args[2] == "light");
            if (args.Length >= 2 && args[0] == "--preview-settings") return PreviewSettings(args[1], args.Length > 2 && args[2] == "light");

            bool first;
            string user = Environment.UserName.Replace("\\", "_");
            bool quiet = Array.IndexOf(args, "--startup") >= 0;
            if (Array.IndexOf(args, "--exit") >= 0) return AskRunningToExit(user);
            using (var mutex = new Mutex(true, "GlassDesk_SingleInstance_" + user, out first))
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "GlassDesk_ShowSettings_" + user))
            using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "GlassDesk_Exit_" + user))
            {
                if (!first) { if (!quiet) signal.Set(); return 0; }   // already running: ask it to open settings

                Native.MakeDpiAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Log(e.ExceptionObject as Exception);
                    ShowCrash(e.ExceptionObject as Exception);
                };

                var app = new App(false);
                try { app.Start(); }
                catch (Exception ex)
                {
                    Log(ex);
                    ShowCrash(ex);
                    return 1;
                }
                var invoker = new Control(); invoker.CreateControl();
                ThreadPool.RegisterWaitForSingleObject(signal, delegate
                {
                    try { invoker.BeginInvoke(new Action(delegate { app.ShowSettings(); })); } catch { }
                }, null, Timeout.Infinite, false);
                ThreadPool.RegisterWaitForSingleObject(exitSignal, delegate
                {
                    try { invoker.BeginInvoke(new Action(delegate { app.Exit(); })); } catch { }
                }, null, Timeout.Infinite, true);
                Application.Run(app);
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        /// <summary>Used by the uninstaller / upgrades: closes a running GlassDesk cleanly.</summary>
        static int AskRunningToExit(string user)
        {
            try
            {
                EventWaitHandle ev;
                if (!EventWaitHandle.TryOpenExisting("GlassDesk_Exit_" + user, out ev)) return 0;
                using (ev) ev.Set();
                for (int i = 0; i < 50; i++)   // wait up to 5 s for it to close
                {
                    Mutex m;
                    if (!Mutex.TryOpenExisting("GlassDesk_SingleInstance_" + user, out m)) break;
                    m.Dispose();
                    Thread.Sleep(100);
                }
            }
            catch { }
            return 0;
        }

        static void ShowCrash(Exception ex)
        {
            try
            {
                MessageBox.Show("GlassDesk ran into a problem and has to close.\n\n" + (ex == null ? "Unknown error" : ex.GetType().Name + ": " + ex.Message) +
                    "\n\nDetails were saved to:\n" + Path.Combine(Settings.Folder, "error.log"), "GlassDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        internal static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(Path.Combine(Settings.Folder, "error.log"), DateTime.Now.ToString("s") + "  " + ex + "\r\n\r\n", Encoding.UTF8);
            }
            catch { }
        }

        // Renders every panel to PNG files (used to make the screenshots in the guide).
        static int Preview(string dir, bool light)
        {
            Directory.CreateDirectory(dir);
            var app = new App(true);
            app.StartPreview(!light);
            var sb = new StringBuilder();
            foreach (var w in app.Windows)
            {
                if (!w.Wanted) continue;
                using (var bmp = w.RenderBitmap()) bmp.Save(Path.Combine(dir, w.Id + ".png"), ImageFormat.Png);
                sb.AppendLine(w.Id + " " + w.Left + " " + w.Top + " " + w.Width + " " + w.Height);
            }
            File.WriteAllText(Path.Combine(dir, "layout.txt"), sb.ToString());
            using (var logo = App.LogoBitmap(256, app.Theme.Accent)) logo.Save(Path.Combine(dir, "logo.png"), ImageFormat.Png);
            return 0;
        }

        static int PreviewSettings(string dir, bool light)
        {
            Directory.CreateDirectory(dir);
            Application.EnableVisualStyles();
            var app = new App(true);
            app.StartPreview(!light);
            var f = new SettingsForm(app, 0);
            f.StartPosition = FormStartPosition.Manual; f.Location = new System.Drawing.Point(0, 0);
            f.Show();
            for (int i = 0; i < 6; i++)
            {
                f.ShowPage(i);
                var until = DateTime.Now.AddMilliseconds(700);
                while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(20); }
                var psi = new System.Diagnostics.ProcessStartInfo("import", "-window root -crop " + f.Width + "x" + f.Height + "+" + f.Left + "+" + f.Top + " " + Path.Combine(dir, "settings" + i + ".png")) { UseShellExecute = false };
                using (var pr = System.Diagnostics.Process.Start(psi)) pr.WaitForExit();
            }
            return 0;
        }
    }
}
