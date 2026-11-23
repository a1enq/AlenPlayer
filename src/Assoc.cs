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
    // ───────────────────────────── Ассоциации и регистрация ─────────────────────────────
    // Всё пишется только в HKCU (без прав администратора). Окончательный выбор «по умолчанию»
    // Windows 10/11 разрешает сделать только пользователю в Параметрах — их мы и открываем.
    static class Assoc
    {
        public const string AppName = "AlenPlayer";
        const string CapKey = @"Software\AlenPlayer\Capabilities";
        const string ThumbIid = "{e357fccd-a995-4576-b01f-234630154e96}";
        static readonly string Clsid = "{" + ThumbProvider.ClsidStr + "}";

        static string Exe { get { return Assembly.GetExecutingAssembly().Location; } }
        static RegistryKey HK { get { return Registry.CurrentUser; } }

        static Tuple<string, string, HashSet<string>, MediaKind>[] Groups
        {
            get
            {
                return new[]
                {
                    Tuple.Create("AlenPlayer.Video", L.T("Видеофайл", "Video file"), Formats.Video, MediaKind.Video),
                    Tuple.Create("AlenPlayer.Audio", L.T("Аудиофайл", "Audio file"), Formats.Audio, MediaKind.Audio),
                    Tuple.Create("AlenPlayer.Image", L.T("Изображение", "Image"), Formats.Image, MediaKind.Image),
                };
            }
        }

        static IEnumerable<string> ThumbExts()
        {
            return Formats.Video.Concat(Formats.Audio).Concat(Formats.Image.Where(e => !Formats.NativeThumbImages.Contains(e)));
        }

        public static bool IsRegistered
        {
            get { using (var k = HK.OpenSubKey(CapKey)) return k != null; }
        }

        static string TypeIcon(MediaKind k)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlenPlayer", "icons");
            string p = Path.Combine(dir, k.ToString().ToLowerInvariant() + ".ico");
            try { if (!File.Exists(p)) { Directory.CreateDirectory(dir); IconMaker.WriteIco(p, k); } }
            catch { return "\"" + Exe + "\",0"; }
            return p;
        }

        public static void Register()
        {
            string exe = Exe, cmd = "\"" + exe + "\" \"%1\"", appIcon = "\"" + exe + "\",0";
            using (var cls = HK.CreateSubKey(@"Software\Classes"))
            using (var cap = HK.CreateSubKey(CapKey))
            using (var fa = cap.CreateSubKey("FileAssociations"))
            using (var app = cls.CreateSubKey(@"Applications\" + Path.GetFileName(exe)))
            using (var sup = app.CreateSubKey("SupportedTypes"))
            {
                cap.SetValue("ApplicationName", AppName);
                cap.SetValue("ApplicationDescription", L.T("Портативный плеер для видео, музыки и фото", "Portable player for video, music and photos"));
                cap.SetValue("ApplicationIcon", appIcon);
                app.SetValue("FriendlyAppName", AppName);
                using (var c = app.CreateSubKey(@"shell\open\command")) c.SetValue("", cmd);
                using (var d = app.CreateSubKey("DefaultIcon")) d.SetValue("", appIcon);

                foreach (var g in Groups)
                {
                    using (var p = cls.CreateSubKey(g.Item1))
                    {
                        p.SetValue("", g.Item2 + " (" + AppName + ")");
                        p.SetValue("FriendlyTypeName", g.Item2);
                        using (var d = p.CreateSubKey("DefaultIcon")) d.SetValue("", TypeIcon(g.Item4));
                        using (var c = p.CreateSubKey(@"shell\open")) c.SetValue("FriendlyAppName", AppName);
                        using (var c = p.CreateSubKey(@"shell\open\command")) c.SetValue("", cmd);
                    }
                    foreach (var ext in g.Item3)
                    {
                        fa.SetValue(ext, g.Item1);
                        sup.SetValue(ext, "");
                        using (var o = cls.CreateSubKey(ext + @"\OpenWithProgids"))
                            o.SetValue(g.Item1, new byte[0], RegistryValueKind.None);
                    }
                }
            }
            using (var ra = HK.CreateSubKey(@"Software\RegisteredApplications")) ra.SetValue(AppName, CapKey);
            RegisterThumbnails();
        }

        // Проводник загружает обработчик миниатюр к себе в процесс и держит файл открытым.
        // Поэтому регистрируем не сам exe, а его копию в %LOCALAPPDATA%\AlenPlayer\shell —
        // папку программы Проводник не блокирует, её можно обновлять и удалять.
        public const string AppKey = @"Software\AlenPlayer";
        static string ShellDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlenPlayer", "shell"); }
        }
        static string ShellCopy
        {
            get { return Path.Combine(ShellDir, "AlenPlayer-" + Assembly.GetExecutingAssembly().GetName().Version + ".exe"); }
        }

        public static void RegisterThumbnails()
        {
            var asm = Assembly.GetExecutingAssembly();
            string copy = ShellCopy;
            Directory.CreateDirectory(ShellDir);
            try { File.Copy(Exe, copy, true); }
            catch { if (!File.Exists(copy)) return; } // копия этой версии уже загружена Проводником — оставляем её
            foreach (var f in Directory.GetFiles(ShellDir, "*.exe"))
                if (!string.Equals(f, copy, StringComparison.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }
            using (var k = HK.CreateSubKey(AppKey)) k.SetValue("ExePath", Exe);
            using (var k = HK.CreateSubKey(@"Software\Classes\CLSID\" + Clsid))
            {
                k.SetValue("", "AlenPlayer Thumbnail Provider");
                k.SetValue("DisableProcessIsolation", 1, RegistryValueKind.DWord);
                using (var ip = k.CreateSubKey("InprocServer32"))
                {
                    ip.SetValue("", "mscoree.dll");
                    ip.SetValue("ThreadingModel", "Both");
                    ip.SetValue("Class", typeof(ThumbProvider).FullName);
                    ip.SetValue("Assembly", asm.FullName);
                    ip.SetValue("RuntimeVersion", "v4.0.30319");
                    ip.SetValue("CodeBase", new Uri(copy).AbsoluteUri);
                }
            }
            foreach (var ext in ThumbExts())
                using (var k = HK.CreateSubKey(@"Software\Classes\" + ext + @"\ShellEx\" + ThumbIid))
                    k.SetValue("", Clsid);
            Native.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        // Миниатюры регистрируем автоматически; при переносе exe — обновляем путь.
        public static void EnsureThumbnails()
        {
            using (var k = HK.OpenSubKey(@"Software\Classes\CLSID\" + Clsid + @"\InprocServer32"))
            using (var a = HK.OpenSubKey(AppKey))
            {
                string cb = k != null ? k.GetValue("CodeBase") as string : null;
                string asm = k != null ? k.GetValue("Assembly") as string : null;
                string exe = a != null ? a.GetValue("ExePath") as string : null;
                if (cb == new Uri(ShellCopy).AbsoluteUri && asm == Assembly.GetExecutingAssembly().FullName &&
                    string.Equals(exe, Exe, StringComparison.OrdinalIgnoreCase) && File.Exists(ShellCopy)) return;
            }
            RegisterThumbnails();
        }

        public static void Unregister()
        {
            using (var cls = HK.OpenSubKey(@"Software\Classes", true))
            {
                if (cls != null)
                {
                    foreach (var g in Groups)
                    {
                        cls.DeleteSubKeyTree(g.Item1, false);
                        foreach (var ext in g.Item3)
                            using (var o = cls.OpenSubKey(ext + @"\OpenWithProgids", true))
                                if (o != null) o.DeleteValue(g.Item1, false);
                    }
                    foreach (var ext in ThumbExts())
                        using (var k = cls.OpenSubKey(ext + @"\ShellEx\" + ThumbIid, true))
                            if (k != null && string.Equals(k.GetValue("") as string, Clsid, StringComparison.OrdinalIgnoreCase))
                                k.DeleteValue("", false);
                    cls.DeleteSubKeyTree(@"CLSID\" + Clsid, false);
                    cls.DeleteSubKeyTree(@"Applications\" + Path.GetFileName(Exe), false);
                }
            }
            HK.DeleteSubKeyTree(AppKey, false);
            using (var ra = HK.OpenSubKey(@"Software\RegisteredApplications", true))
                if (ra != null) ra.DeleteValue(AppName, false);
            Native.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            // копия, загруженная Проводником, удалится при следующем запуске/перезагрузке
            try { foreach (var f in Directory.GetFiles(ShellDir)) try { File.Delete(f); } catch { } } catch { }
        }

        public static void RefreshIfMoved()
        {
            if (!IsRegistered) return;
            using (var k = HK.OpenSubKey(@"Software\Classes\AlenPlayer.Video\shell\open\command"))
            {
                string v = k != null ? k.GetValue("") as string : null;
                if (v == null || v.IndexOf(Exe, StringComparison.OrdinalIgnoreCase) < 0) Register();
            }
        }

        public static void OpenSettings()
        {
            try { Process.Start("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppName)); }
            catch { try { Process.Start("ms-settings:defaultapps"); } catch { } }
        }
    }
}
