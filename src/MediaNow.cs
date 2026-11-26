// GlassDesk 2.1.1 - reads "what's playing" straight from Windows, inside GlassDesk itself.
// Before 2.1.1 this was a hidden PowerShell script, and antivirus apps (Microsoft Defender among them)
// took that for malware. Now there is no script and no extra process: GlassDesk asks the same Windows
// media service the volume flyout uses, on a background thread, every 1.5 seconds.
// It only reads the song's title, artist, position and cover picture. Nothing is sent anywhere.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GlassDesk
{
    internal sealed class MediaNow
    {
        readonly string coverFile;
        readonly Action<string> onLine;     // same lines as the old helper: N, or S<tab>status<tab>title<tab>artist<tab>pos<tab>dur<tab>art
        volatile bool running;
        Thread thread;

        public MediaNow(string coverFile, Action<string> onLine) { this.coverFile = coverFile; this.onLine = onLine; }

        public void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "GlassDesk music" };
            thread.Start();
        }
        public void Stop() { running = false; }

        // ---- WinRT through reflection (GlassDesk is built without the Windows SDK) ----
        static MethodInfo asTask, asStream;
        static Type T(string name, string winmd)
        {
            return Type.GetType(name + ", " + winmd + ", ContentType=WindowsRuntime", true);
        }
        static void Load()
        {
            if (asTask != null) return;
            var rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            asTask = rt.GetType("System.WindowsRuntimeSystemExtensions", true).GetMethods()
                .First(m => m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
            asStream = rt.GetType("System.IO.WindowsRuntimeStreamExtensions", true).GetMethods()
                .First(m => m.Name == "AsStreamForRead" && m.GetParameters().Length == 1);
        }
        static object Await(object op, Type result)
        {
            var task = (Task)asTask.MakeGenericMethod(result).Invoke(null, new[] { op });
            if (!task.Wait(5000)) return null;
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }
        // looks the member up on the object's own type, and on the type Windows says it has if that fails
        static object Get(object o, Type declared, string prop)
        {
            if (o == null) return null;
            var pi = o.GetType().GetProperty(prop) ?? declared.GetProperty(prop);
            return pi.GetValue(o, null);
        }
        static object Call(object o, Type declared, string method)
        {
            if (o == null) return null;
            var mi = o.GetType().GetMethod(method, Type.EmptyTypes) ?? declared.GetMethod(method, Type.EmptyTypes);
            return mi.Invoke(o, null);
        }
        static string Clean(object s) { return s == null ? "" : s.ToString().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        void Run()
        {
            object mgr = null;
            Type tMgr = null, tSes = null, tProps = null, tInfo = null, tTl = null, tRef = null, tStream = null;
            string last = "#", lastHash = ""; int tries = 0, fails = 0;
            using (var md5 = MD5.Create())
            while (running)
            {
                string line = "N";
                try
                {
                    if (mgr == null)
                    {
                        Load();
                        const string mc = "Windows.Media.Control", ss = "Windows.Storage.Streams", g = "Windows.Media.Control.GlobalSystemMediaTransportControlsSession";
                        tMgr = T(g + "Manager", mc); tSes = T(g, mc); tProps = T(g + "MediaProperties", mc);
                        tInfo = T(g + "PlaybackInfo", mc); tTl = T(g + "TimelineProperties", mc);
                        tRef = T(ss + ".IRandomAccessStreamReference", ss); tStream = T(ss + ".IRandomAccessStreamWithContentType", ss);
                        mgr = Await(tMgr.GetMethod("RequestAsync").Invoke(null, null), tMgr);
                    }
                    var s = Call(mgr, tMgr, "GetCurrentSession");
                    if (s != null)
                    {
                        var p = Await(Call(s, tSes, "TryGetMediaPropertiesAsync"), tProps);
                        var info = Call(s, tSes, "GetPlaybackInfo");
                        var tl = Call(s, tSes, "GetTimelineProperties");
                        string status = Clean(Get(info, tInfo, "PlaybackStatus"));
                        double pos = ((TimeSpan)Get(tl, tTl, "Position")).TotalSeconds;
                        var upd = (DateTimeOffset)Get(tl, tTl, "LastUpdatedTime");
                        if (status == "Playing" && upd.Year > 2000) pos += (DateTimeOffset.Now - upd).TotalSeconds;
                        double dur = ((TimeSpan)Get(tl, tTl, "EndTime") - (TimeSpan)Get(tl, tTl, "StartTime")).TotalSeconds;
                        if (pos > dur && dur > 0) pos = dur;
                        string title = Clean(Get(p, tProps, "Title")), artist = Clean(Get(p, tProps, "Artist"));
                        string art = "0";
                        // a new song: apps often send the picture a few seconds after the title, so keep looking for a while
                        if (title + "|" + artist != last) { last = title + "|" + artist; tries = 8; art = "-"; lastHash = ""; }
                        if (tries > 0)
                        {
                            tries--;
                            try
                            {
                                var thumb = Get(p, tProps, "Thumbnail");
                                if (thumb != null)
                                {
                                    var st = Await(Call(thumb, tRef, "OpenReadAsync"), tStream);
                                    using (var input = (Stream)asStream.Invoke(null, new[] { st }))
                                    using (var ms = new MemoryStream())
                                    {
                                        input.CopyTo(ms);
                                        byte[] bytes = ms.ToArray();
                                        string h = BitConverter.ToString(md5.ComputeHash(bytes));
                                        if (bytes.Length > 100 && h != lastHash)
                                        {
                                            lastHash = h;
                                            string tmp = coverFile + ".tmp";
                                            File.WriteAllBytes(tmp, bytes);
                                            if (File.Exists(coverFile)) File.Delete(coverFile);
                                            File.Move(tmp, coverFile);
                                            art = "1";
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        line = "S\t" + status + "\t" + title + "\t" + artist + "\t" + (int)Math.Max(0, pos) + "\t" + (int)Math.Max(0, dur) + "\t" + art;
                    }
                    fails = 0;
                }
                catch (Exception ex)
                {
                    // Windows without the media service (very old Windows 10): stop quietly after a few tries
                    if (++fails == 1) Program.Log(ex);
                    mgr = null;
                    if (fails >= 5) { running = false; }
                }
                try { onLine(line); } catch { }
                for (int i = 0; i < 15 && running; i++) Thread.Sleep(100);
            }
        }
    }
}
