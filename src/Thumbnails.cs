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
    // ───────────────────────────── Миниатюры для Проводника ─────────────────────────────
    // В Проводнике работает только лёгкая прослойка; кадр достаёт отдельный процесс
    // «AlenPlayer.exe --thumb» с пониженным приоритетом — движок не грузится в Проводник,
    // а битый файл не может его уронить. Одновременно — не больше двух таких процессов.
    [ComImport, Guid("e357fccd-a995-4576-b01f-234630154e96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider { [PreserveSig] int GetThumbnail(uint cx, out IntPtr hbmp, out int alpha); }

    [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithFile { [PreserveSig] int Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attrs);
        void Compare(IShellItem psi, uint hint, out int order);
    }

    [ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithItem { [PreserveSig] int Initialize(IShellItem item, uint mode); }

    [ComVisible(true), Guid(ThumbProvider.ClsidStr), ClassInterface(ClassInterfaceType.None)]
    public class ThumbProvider : IThumbnailProvider, IInitializeWithFile, IInitializeWithItem
    {
        public const string ClsidStr = "6E1C7F2A-4B7D-4C1E-9A55-A1E7A11E0002";
        const int E_FAIL = unchecked((int)0x80004005);
        string path;

        int IInitializeWithFile.Initialize(string p, uint mode) { path = p; return 0; }

        int IInitializeWithItem.Initialize(IShellItem item, uint mode)
        {
            try
            {
                IntPtr s;
                item.GetDisplayName(0x80058000, out s); // SIGDN_FILESYSPATH
                path = Marshal.PtrToStringUni(s);
                Marshal.FreeCoTaskMem(s);
                return 0;
            }
            catch { return E_FAIL; }
        }

        public int GetThumbnail(uint cx, out IntPtr hbmp, out int alpha)
        {
            hbmp = IntPtr.Zero; alpha = 0;
            if (string.IsNullOrEmpty(path)) return E_FAIL;
            string png = Path.Combine(Path.GetTempPath(), "alenthumb-" + Guid.NewGuid().ToString("N") + ".png");
            // Путь к установленному AlenPlayer.exe (рядом с ним libmpv-2.dll)
            string exe = null;
            using (var k = Registry.CurrentUser.OpenSubKey(Assoc.AppKey))
                if (k != null) exe = k.GetValue("ExePath") as string;
            if (exe == null || !File.Exists(exe)) return E_FAIL;
            try
            {
                using (var sem = new Semaphore(2, 2, @"Local\AlenPlayerThumbs"))
                {
                    if (!sem.WaitOne(30000)) return E_FAIL;
                    try
                    {
                        int size = (int)Math.Max(32, Math.Min(1024, cx));
                        var psi = new ProcessStartInfo(exe,
                            "--thumb \"" + path + "\" " + size + " \"" + png + "\"")
                        { UseShellExecute = false, CreateNoWindow = true };
                        using (var p = Process.Start(psi))
                        {
                            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                            if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return E_FAIL; }
                        }
                    }
                    finally { sem.Release(); }
                }
                if (!File.Exists(png)) return E_FAIL;
                using (var ms = new MemoryStream(File.ReadAllBytes(png)))
                using (var img = Image.FromStream(ms))
                using (var b = Thumbnailer.Fit(img, (int)cx))
                {
                    hbmp = Thumbnailer.ToDib(b);
                    alpha = 2; // WTSAT_ARGB
                }
                return hbmp == IntPtr.Zero ? E_FAIL : 0;
            }
            catch { return E_FAIL; }
            finally { try { File.Delete(png); } catch { } }
        }
    }

    static class Thumbnailer
    {
        // Режим отдельного процесса: AlenPlayer.exe --thumb <файл> <размер> <png>
        public static int Child(string file, int cx, string outPng)
        {
            string err;
            if (!Engine.Load(out err)) return 2;
            var kind = Formats.Of(file);
            string dir = Path.Combine(Path.GetTempPath(), "alenthumb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            IntPtr h = MpvApi.mpv_create();
            if (h == IntPtr.Zero) return 3;
            try
            {
                MpvApi.Lean(h);
                foreach (var kv in new[]
                {
                    "vo=image", "vo-image-format=png", "vo-image-png-compression=1", "ao=null", "aid=no", "sid=no",
                    "frames=1", "hwdec=no", "hr-seek=no", "vd-lavc-threads=2",
                    "audio-display=embedded-first", "sub-auto=no", "audio-file-auto=no", "image-display-duration=0",
                })
                {
                    int i = kv.IndexOf('=');
                    MpvApi.Opt(h, kv.Substring(0, i), kv.Substring(i + 1));
                }
                MpvApi.Opt(h, "vo-image-outdir", dir);
                if (Environment.GetEnvironmentVariable("ALEN_DEBUG") != null) MpvApi.Opt(h, "log-file", Path.Combine(Path.GetTempPath(), "alen-thumb.log"));
                // Большие кадры сразу уменьшаем фильтром — меньше памяти и времени
                MpvApi.Opt(h, "vf", "scale=w=" + cx + ":h=" + cx + ":force_original_aspect_ratio=decrease:flags=bilinear");
                if (kind == MediaKind.Video || kind == MediaKind.None) MpvApi.Opt(h, "start", "20%");
                if (MpvApi.mpv_initialize(h) < 0) return 4;
                MpvApi.Run(h, false, new[] { "loadfile", file });
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 15000)
                {
                    var e = (MpvEvent)Marshal.PtrToStructure(MpvApi.mpv_wait_event(h, 0.5), typeof(MpvEvent));
                    if (e.Id == 7 || e.Id == 1) break;
                }
            }
            finally { MpvApi.mpv_terminate_destroy(h); }
            try
            {
                var f = Directory.GetFiles(dir, "*.png").OrderBy(x => x).FirstOrDefault();
                if (f == null) return 5;
                File.Copy(f, outPng, true);
                return 0;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static Bitmap Fit(Image img, int cx)
        {
            double s = Math.Min(1.0, (double)cx / Math.Max(img.Width, img.Height));
            int w = Math.Max(1, (int)Math.Round(img.Width * s)), h = Math.Max(1, (int)Math.Round(img.Height * s));
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(img, new Rectangle(0, 0, w, h));
            }
            return b;
        }

        public static IntPtr ToDib(Bitmap b)
        {
            var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = b.Width, biHeight = -b.Height, biPlanes = 1, biBitCount = 32 };
            IntPtr bits;
            IntPtr hbmp = Native.CreateDIBSection(IntPtr.Zero, ref bi, 0, out bits, IntPtr.Zero, 0);
            if (hbmp == IntPtr.Zero) return IntPtr.Zero;
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[b.Width * 4];
                for (int y = 0; y < b.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    Marshal.Copy(row, 0, bits + y * row.Length, row.Length);
                }
            }
            finally { b.UnlockBits(data); }
            return hbmp;
        }
    }
}
