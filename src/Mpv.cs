using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

namespace Alen
{
    // ───────────────────────────── Движок ─────────────────────────────
    static class Engine
    {
        static bool loaded;

        public static string ExeDir { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }

        // libmpv-2.dll лежит рядом с AlenPlayer.exe
        public static bool Load(out string err)
        {
            err = null;
            if (loaded) return true;
            string dll = Path.Combine(ExeDir, "libmpv-2.dll");
            if (!File.Exists(dll))
            {
                err = L.T("Рядом с AlenPlayer.exe нет файла libmpv-2.dll.\n\n" +
                          "Скачайте архив AlenPlayer целиком и распакуйте все файлы в одну папку.",
                          "libmpv-2.dll was not found next to AlenPlayer.exe.\n\n" +
                          "Download the whole AlenPlayer archive and extract all files into one folder.");
                return false;
            }
            if (Native.LoadLibrary(dll) == IntPtr.Zero)
            {
                err = L.T("Не удалось загрузить libmpv-2.dll (код ", "Failed to load libmpv-2.dll (code ") + Marshal.GetLastWin32Error() + ").\n\n" +
                      L.T("Нужна 64-битная Windows 10 или 11.", "64-bit Windows 10 or 11 is required.");
                return false;
            }
            loaded = true;
            return true;
        }
    }

    static class MpvApi
    {
        const string L = "libmpv-2.dll";
        const CallingConvention C = CallingConvention.Cdecl;
        [DllImport(L, CallingConvention = C)] public static extern IntPtr mpv_create();
        [DllImport(L, CallingConvention = C)] public static extern int mpv_initialize(IntPtr h);
        [DllImport(L, CallingConvention = C)] public static extern int mpv_set_option_string(IntPtr h, byte[] name, byte[] data);
        [DllImport(L, CallingConvention = C)] public static extern int mpv_command(IntPtr h, IntPtr[] args);
        [DllImport(L, CallingConvention = C)] public static extern int mpv_command_async(IntPtr h, ulong ud, IntPtr[] args);
        [DllImport(L, CallingConvention = C)] public static extern IntPtr mpv_get_property_string(IntPtr h, byte[] name);
        [DllImport(L, CallingConvention = C)] public static extern int mpv_get_property_async(IntPtr h, ulong ud, byte[] name, int format);
        [DllImport(L, CallingConvention = C)] public static extern int mpv_observe_property(IntPtr h, ulong ud, byte[] name, int format);
        [DllImport(L, CallingConvention = C)] public static extern IntPtr mpv_wait_event(IntPtr h, double timeout);
        [DllImport(L, CallingConvention = C)] public static extern void mpv_free(IntPtr p);
        [DllImport(L, CallingConvention = C)] public static extern void mpv_terminate_destroy(IntPtr h);
        [DllImport(L, CallingConvention = C)] public static extern int mpv_stream_cb_add_ro(IntPtr h, byte[] protocol, IntPtr ud, IntPtr openFn);

        public static byte[] U(string s) { return Encoding.UTF8.GetBytes((s ?? "") + "\0"); }

        public static string S(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var b = new byte[n];
            Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        public static void Opt(IntPtr h, string n, string v) { mpv_set_option_string(h, U(n), U(v)); }

        // Вызов команды с UTF-8 аргументами (sync или async)
        public static int Run(IntPtr h, bool async, string[] a)
        {
            if (h == IntPtr.Zero) return -1;
            var p = new IntPtr[a.Length + 1];
            try
            {
                for (int i = 0; i < a.Length; i++)
                {
                    byte[] b = U(a[i]);
                    p[i] = Marshal.AllocHGlobal(b.Length);
                    Marshal.Copy(b, 0, p[i], b.Length);
                }
                return async ? mpv_command_async(h, 0, p) : mpv_command(h, p);
            }
            finally { foreach (var x in p) if (x != IntPtr.Zero) Marshal.FreeHGlobal(x); }
        }

        // Общие настройки «лёгкого» движка: без скриптов, конфигов и лишних модулей.
        public static void Lean(IntPtr h)
        {
            foreach (var kv in new[]
            {
                "config=no", "load-scripts=no", "osc=no", "ytdl=no", "terminal=no", "input-default-bindings=no",
                "load-stats-overlay=no", "load-console=no", "load-auto-profiles=no", "load-select=no",
                "load-positioning=no", "load-commands=no", "load-context-menu=no",
            })
            {
                int i = kv.IndexOf('=');
                Opt(h, kv.Substring(0, i), kv.Substring(i + 1));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct MpvEvent { public int Id; public int Error; public ulong Ud; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] struct MpvEventProperty { public IntPtr Name; public int Format; public IntPtr Data; }

    class Player
    {
        IntPtr h;
        Thread thread;
        readonly Control ui;
        public event Action<string, object> PropertyChanged;
        public event Action FileLoaded;
        public event Action<int, int> EndFile;

        public Player(Control ui)
        {
            this.ui = ui;
            h = MpvApi.mpv_create();
            if (h == IntPtr.Zero) throw new Exception("mpv_create failed");
            MpvApi.Lean(h);
        }

        public void Opt(string n, string v) { MpvApi.Opt(h, n, v); }

        public void Start()
        {
            int r = MpvApi.mpv_initialize(h);
            if (r < 0) throw new Exception("mpv_initialize: " + r);
            // time-pos не наблюдаем (это события на каждый кадр) — опрашиваем, только когда панель видна.
            Observe("duration", 5);
            Observe("pause", 3);
            Observe("volume", 5);
            Observe("mute", 3);
            Observe("speed", 5);
            Observe("eof-reached", 3);
            Observe("current-tracks/video/image", 3);
            thread = new Thread(Loop) { IsBackground = true, Name = "mpv-events" };
            thread.Start();
        }

        void Observe(string n, int fmt) { MpvApi.mpv_observe_property(h, 0, MpvApi.U(n), fmt); }

        public void Cmd(params string[] a) { MpvApi.Run(h, true, a); }
        public void Set(string n, string v) { Cmd("set", n, v); }
        public void Set(string n, double v) { Cmd("set", n, v.ToString("0.######", CultureInfo.InvariantCulture)); }
        public void GetAsync(string n) { if (h != IntPtr.Zero) MpvApi.mpv_get_property_async(h, 0, MpvApi.U(n), 5); }

        public string Get(string n)
        {
            if (h == IntPtr.Zero) return null;
            IntPtr p = MpvApi.mpv_get_property_string(h, MpvApi.U(n));
            if (p == IntPtr.Zero) return null;
            string s = MpvApi.S(p);
            MpvApi.mpv_free(p);
            return s;
        }

        public double GetD(string n, double def)
        {
            double d;
            string s = Get(n);
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def;
        }

        void Loop()
        {
            while (true)
            {
                IntPtr ep = MpvApi.mpv_wait_event(h, -1);
                var e = (MpvEvent)Marshal.PtrToStructure(ep, typeof(MpvEvent));
                if (e.Id == 1) break; // SHUTDOWN
                if ((e.Id == 22 || (e.Id == 3 && e.Error >= 0)) && e.Data != IntPtr.Zero) // PROPERTY_CHANGE / GET_PROPERTY_REPLY
                {
                    var pr = (MpvEventProperty)Marshal.PtrToStructure(e.Data, typeof(MpvEventProperty));
                    string name = MpvApi.S(pr.Name);
                    object val = null;
                    if (pr.Data != IntPtr.Zero)
                    {
                        switch (pr.Format)
                        {
                            case 1: val = MpvApi.S(Marshal.ReadIntPtr(pr.Data)); break;
                            case 3: val = Marshal.ReadInt32(pr.Data) != 0; break;
                            case 4: val = Marshal.ReadInt64(pr.Data); break;
                            case 5: val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(pr.Data)); break;
                        }
                    }
                    Post(() => { var hnd = PropertyChanged; if (hnd != null) hnd(name, val); });
                }
                else if (e.Id == 8)
                {
                    Post(() => { var hnd = FileLoaded; if (hnd != null) hnd(); });
                }
                else if (e.Id == 7 && e.Data != IntPtr.Zero)
                {
                    int reason = Marshal.ReadInt32(e.Data), error = Marshal.ReadInt32(e.Data, 4);
                    Post(() => { var hnd = EndFile; if (hnd != null) hnd(reason, error); });
                }
            }
        }

        void Post(Action a)
        {
            try { if (ui.IsHandleCreated && !ui.IsDisposed) ui.BeginInvoke(a); } catch { }
        }

        // Вызывается из фонового потока; UI-поток в это время прокачивает сообщения.
        public void Quit(params string[] preCommands)
        {
            if (h == IntPtr.Zero) return;
            foreach (var c in preCommands) Cmd(c);
            Cmd("quit");
            if (thread != null) thread.Join(3000);
            MpvApi.mpv_terminate_destroy(h);
            h = IntPtr.Zero;
        }
    }
}
