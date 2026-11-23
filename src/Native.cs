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
    // ───────────────────────────── Native ─────────────────────────────
    static class Native
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("user32")] public static extern bool EnableWindow(IntPtr h, bool enable);
        [DllImport("user32")] public static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
        [DllImport("user32")] public static extern bool ReleaseCapture();
        [DllImport("user32")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("shlwapi", CharSet = CharSet.Unicode)] public static extern int StrCmpLogicalW(string a, string b);
        [DllImport("uxtheme", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string id);
        [DllImport("shell32")] public static extern void SHChangeNotify(int e, int f, IntPtr a, IntPtr b);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLEND { public byte Op, Flags, Alpha, Format; }
        [DllImport("user32", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int key, ref BLEND blend, int flags);
        [DllImport("user32", EntryPoint = "UpdateLayeredWindow", SetLastError = true)]
        public static extern bool UpdateLayeredAlpha(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pptSrc, int key, ref BLEND blend, int flags);
        [DllImport("user32")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32")] public static extern bool DeleteObject(IntPtr obj);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }
        [DllImport("gdi32")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    }
}
