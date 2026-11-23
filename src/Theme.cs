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
    // ───────────────────────────── Тема ─────────────────────────────
    static class Theme
    {
        public static float Scale = 1f;
        public static int S(float v) { return (int)Math.Round(v * Scale); }
        public static float SF(float v) { return v * Scale; }

        public static readonly Color Bg = Color.FromArgb(12, 13, 19);
        public static readonly Color Surface = Color.FromArgb(19, 21, 30);
        public static readonly Color Surface2 = Color.FromArgb(31, 34, 49);
        public static readonly Color Hover = Color.FromArgb(38, 42, 60);
        public static readonly Color Line = Color.FromArgb(34, 37, 52);
        public static readonly Color Text = Color.FromArgb(236, 238, 246);
        public static readonly Color Icon = Color.FromArgb(200, 204, 218);
        public static readonly Color Dim = Color.FromArgb(132, 138, 160);
        public static readonly Color A1 = Color.FromArgb(139, 92, 246);
        public static readonly Color A2 = Color.FromArgb(34, 211, 238);
        public const string BgHex = "#0C0D13";

        public static Font UI, UIBold, Small, Big, Tiny, OsdFont, OsdSmall;

        public static void Init()
        {
            UI = new Font("Segoe UI", 9.5f);
            UIBold = new Font("Segoe UI Semibold", 10.5f);
            Small = new Font("Segoe UI", 8.5f);
            Tiny = new Font("Segoe UI Semibold", 7.5f);
            Big = new Font("Segoe UI Light", 19f);
            // Для слоёных окон — в пикселях, чтобы масштаб не зависел от DPI битмапа
            OsdFont = new Font("Segoe UI", SF(13), GraphicsUnit.Pixel);
            OsdSmall = new Font("Segoe UI Semibold", SF(8.5f), GraphicsUnit.Pixel);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static LinearGradientBrush Grad(RectangleF r, float angle)
        {
            if (r.Width < 1) r.Width = 1;
            if (r.Height < 1) r.Height = 1;
            return new LinearGradientBrush(r, A1, A2, angle);
        }

        public static string Time(double s)
        {
            if (double.IsNaN(s) || double.IsInfinity(s) || s < 0) s = 0;
            long t = (long)s, hh = t / 3600, mm = (t / 60) % 60, ss = t % 60;
            return hh > 0 ? string.Format("{0}:{1:00}:{2:00}", hh, mm, ss) : string.Format("{0:00}:{1:00}", mm, ss);
        }
    }
}
