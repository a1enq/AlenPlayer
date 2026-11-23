// AlenPlayer — портативный медиаплеер для Windows.
// Движок: libmpv (FFmpeg + GPU-рендер, аппаратное декодирование) — libmpv-2.dll рядом с exe.
// Интерфейс: WinForms на .NET Framework 4.8 (встроен в Windows 10/11).
// Этот же exe служит обработчиком миниатюр для Проводника (COM, IThumbnailProvider).

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

[assembly: AssemblyTitle("AlenPlayer")]
[assembly: AssemblyProduct("AlenPlayer")]
[assembly: AssemblyDescription("Portable media player: video, audio, photos")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

namespace Alen
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--make-icon") { IconMaker.WriteAppIco(args[1]); return 0; }
            if (args.Length == 4 && args[0] == "--thumb") { int sz; return int.TryParse(args[2], out sz) ? Thumbnailer.Child(args[1], sz, args[3]) : 1; }
            if (args.Length == 1 && args[0] == "--register") { Assoc.Register(); return 0; }
            if (args.Length == 1 && args[0] == "--unregister") { Assoc.Unregister(); return 0; }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Crash(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception, true);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;
            Theme.Init();
            ToolStripManager.Renderer = new DarkRenderer();

            Settings.MigrateOld();
            L.Init(Settings.Load().Lang);

            string err;
            if (!Engine.Load(out err))
            {
                MessageBox.Show(err, "AlenPlayer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            // Регистрируемся только из рабочей копии (рядом есть движок)
            try { Assoc.EnsureThumbnails(); Assoc.RefreshIfMoved(); } catch { }
            Application.Run(new MainForm(args));
            return 0;
        }

        static void Crash(Exception ex, bool fatal)
        {
            string log = Path.Combine(Settings.Dir, "crash.log");
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(log, DateTime.Now.ToString("s") + "\r\n" + ex + "\r\n\r\n");
            }
            catch { }
            if (!fatal)
                MessageBox.Show(L.T("Произошла ошибка: ", "An error occurred: ") + (ex != null ? ex.Message : "?") + L.T("\n\nПодробности: ", "\n\nDetails: ") + log,
                    "AlenPlayer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
