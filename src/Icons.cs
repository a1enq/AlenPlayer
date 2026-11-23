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
    // ───────────────────────────── Иконки ─────────────────────────────
    enum Ico { Play, Pause, Prev, Next, Open, Vol, VolLow, Mute, Full, Unfull, List, More, Min, Max, Restore, Close, Back10, Fwd30, Loop, ChevL, ChevR }

    static class Icons
    {
        static PointF P(float x, float y) { return new PointF(x, y); }

        static void Corner(Graphics g, Pen pen, float x, float y, float dx, float dy)
        {
            g.DrawLines(pen, new[] { P(x + dx, y), P(x, y), P(x, y + dy) });
        }

        public static void Draw(Graphics g, Ico ic, float cx, float cy, float r, Color c, bool thin)
        {
            float w = thin ? Math.Max(1f, r * 0.2f) : Math.Max(1.4f, r * 0.17f);
            using (var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var br = new SolidBrush(c))
            {
                switch (ic)
                {
                    case Ico.Play:
                    {
                        var pts = new[] { P(cx - r * 0.42f, cy - r * 0.72f), P(cx + r * 0.78f, cy), P(cx - r * 0.42f, cy + r * 0.72f) };
                        g.FillPolygon(br, pts); g.DrawPolygon(pen, pts);
                        break;
                    }
                    case Ico.Pause:
                    {
                        float bw = r * 0.36f, bh = r * 1.4f;
                        using (var p1 = Theme.Round(new RectangleF(cx - r * 0.58f, cy - bh / 2, bw, bh), bw * 0.3f)) g.FillPath(br, p1);
                        using (var p2 = Theme.Round(new RectangleF(cx + r * 0.22f, cy - bh / 2, bw, bh), bw * 0.3f)) g.FillPath(br, p2);
                        break;
                    }
                    case Ico.Prev:
                    case Ico.Next:
                    {
                        float s = ic == Ico.Prev ? 1 : -1;
                        g.DrawLine(pen, cx - s * r * 0.7f, cy - r * 0.62f, cx - s * r * 0.7f, cy + r * 0.62f);
                        var pts = new[] { P(cx + s * r * 0.65f, cy - r * 0.62f), P(cx + s * r * 0.65f, cy + r * 0.62f), P(cx - s * r * 0.38f, cy) };
                        g.FillPolygon(br, pts); g.DrawPolygon(pen, pts);
                        break;
                    }
                    case Ico.Open:
                    {
                        var pts = new[] { P(cx - r * 0.85f, cy - r * 0.6f), P(cx - r * 0.25f, cy - r * 0.6f), P(cx - r * 0.05f, cy - r * 0.36f),
                                          P(cx + r * 0.85f, cy - r * 0.36f), P(cx + r * 0.85f, cy + r * 0.62f), P(cx - r * 0.85f, cy + r * 0.62f) };
                        g.DrawPolygon(pen, pts);
                        break;
                    }
                    case Ico.Vol:
                    case Ico.VolLow:
                    case Ico.Mute:
                    {
                        float ox = cx - r * 0.25f;
                        var sp = new[] { P(ox - r * 0.6f, cy - r * 0.28f), P(ox - r * 0.22f, cy - r * 0.28f), P(ox + r * 0.22f, cy - r * 0.68f),
                                         P(ox + r * 0.22f, cy + r * 0.68f), P(ox - r * 0.22f, cy + r * 0.28f), P(ox - r * 0.6f, cy + r * 0.28f) };
                        g.FillPolygon(br, sp); g.DrawPolygon(pen, sp);
                        if (ic == Ico.Mute)
                        {
                            float mx = ox + r * 0.78f, k = r * 0.27f;
                            g.DrawLine(pen, mx - k, cy - k, mx + k, cy + k);
                            g.DrawLine(pen, mx + k, cy - k, mx - k, cy + k);
                        }
                        else
                        {
                            float a = r * 0.42f, sx = ox + r * 0.22f;
                            g.DrawArc(pen, sx - a, cy - a, a * 2, a * 2, -50, 100);
                            if (ic == Ico.Vol) { float b = r * 0.8f; g.DrawArc(pen, sx - b, cy - b, b * 2, b * 2, -50, 100); }
                        }
                        break;
                    }
                    case Ico.Full:
                    {
                        float o = r * 0.72f, l = r * 0.42f;
                        Corner(g, pen, cx - o, cy - o, l, l); Corner(g, pen, cx + o, cy - o, -l, l);
                        Corner(g, pen, cx - o, cy + o, l, -l); Corner(g, pen, cx + o, cy + o, -l, -l);
                        break;
                    }
                    case Ico.Unfull:
                    {
                        float i = r * 0.22f, l = r * 0.45f;
                        Corner(g, pen, cx - i, cy - i, -l, -l); Corner(g, pen, cx + i, cy - i, l, -l);
                        Corner(g, pen, cx - i, cy + i, -l, l); Corner(g, pen, cx + i, cy + i, l, l);
                        break;
                    }
                    case Ico.List:
                    {
                        for (int k = -1; k <= 1; k++)
                        {
                            float y = cy + k * r * 0.55f, d = w * 0.75f;
                            g.FillEllipse(br, cx - r * 0.8f - d, y - d, d * 2, d * 2);
                            g.DrawLine(pen, cx - r * 0.35f, y, cx + r * 0.85f, y);
                        }
                        break;
                    }
                    case Ico.More:
                    {
                        float d = r * 0.14f;
                        for (int k = -1; k <= 1; k++) g.FillEllipse(br, cx + k * r * 0.55f - d, cy - d, d * 2, d * 2);
                        break;
                    }
                    case Ico.Min: g.DrawLine(pen, cx - r * 0.5f, cy, cx + r * 0.5f, cy); break;
                    case Ico.Max: g.DrawRectangle(pen, cx - r * 0.48f, cy - r * 0.48f, r * 0.96f, r * 0.96f); break;
                    case Ico.Restore:
                    {
                        float s = r * 0.78f;
                        g.DrawRectangle(pen, cx - r * 0.5f, cy - r * 0.28f, s, s);
                        g.DrawLines(pen, new[] { P(cx - r * 0.28f, cy - r * 0.5f), P(cx + r * 0.5f, cy - r * 0.5f), P(cx + r * 0.5f, cy + r * 0.28f) });
                        break;
                    }
                    case Ico.Close:
                        g.DrawLine(pen, cx - r * 0.48f, cy - r * 0.48f, cx + r * 0.48f, cy + r * 0.48f);
                        g.DrawLine(pen, cx + r * 0.48f, cy - r * 0.48f, cx - r * 0.48f, cy + r * 0.48f);
                        break;
                    case Ico.Back10:
                    case Ico.Fwd30:
                    {
                        // Круговая стрелка с числом, как в проигрывателе Windows
                        bool back = ic == Ico.Back10;
                        float R = r * 0.9f, dir = back ? -1 : 1;
                        using (var tp = new Pen(c, w * 0.85f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            g.DrawArc(tp, cx - R, cy - R, R * 2, R * 2, 270, back ? 290 : -290);
                            float h = R * 0.38f;
                            g.DrawLines(tp, new[] { P(cx - dir * h * 0.2f, cy - R - h * 0.75f), P(cx + dir * h * 0.75f, cy - R), P(cx - dir * h * 0.2f, cy - R + h * 0.75f) });
                        }
                        using (var f = new Font("Segoe UI Semibold", R * 0.92f, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(back ? "10" : "30", f, br, new RectangleF(cx - R, cy - R * 0.85f, R * 2, R * 2), sf);
                        break;
                    }
                    case Ico.Loop:
                    {
                        float x0 = cx - r * 0.8f, x1 = cx + r * 0.8f, y0 = cy - r * 0.42f, y1 = cy + r * 0.42f, hh = r * 0.28f;
                        g.DrawLines(pen, new[] { P(x0, cy + r * 0.08f), P(x0, y0), P(x1 - r * 0.1f, y0) });
                        g.DrawLines(pen, new[] { P(x1 - r * 0.1f - hh, y0 - hh), P(x1 - r * 0.1f, y0), P(x1 - r * 0.1f - hh, y0 + hh) });
                        g.DrawLines(pen, new[] { P(x1, cy - r * 0.08f), P(x1, y1), P(x0 + r * 0.1f, y1) });
                        g.DrawLines(pen, new[] { P(x0 + r * 0.1f + hh, y1 - hh), P(x0 + r * 0.1f, y1), P(x0 + r * 0.1f + hh, y1 + hh) });
                        break;
                    }
                    case Ico.ChevL:
                    case Ico.ChevR:
                    {
                        float s = ic == Ico.ChevL ? 1 : -1;
                        g.DrawLines(pen, new[] { P(cx + s * r * 0.25f, cy - r * 0.6f), P(cx - s * r * 0.35f, cy), P(cx + s * r * 0.25f, cy + r * 0.6f) });
                        break;
                    }
                }
            }
        }
    }

    static class IconMaker
    {
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(r, r.Width * 0.28f))
            using (var br = Theme.Grad(r, 45f))
                g.FillPath(br, path);
            float cx = r.X + r.Width * 0.54f, cy = r.Y + r.Height / 2, s = r.Width * 0.23f;
            var pts = new[] { new PointF(cx - s * 0.8f, cy - s), new PointF(cx + s * 1.05f, cy), new PointF(cx - s * 0.8f, cy + s) };
            using (var wb = new SolidBrush(Color.White)) g.FillPolygon(wb, pts);
            using (var pen = new Pen(Color.White, Math.Max(1f, r.Width * 0.06f)) { LineJoin = LineJoin.Round }) g.DrawPolygon(pen, pts);
        }

        // Значки файлов (показываются, если у файла нет миниатюры)
        static void DrawType(Graphics g, RectangleF r, MediaKind k)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (k == MediaKind.Video) { DrawLogo(g, r); return; }
            using (var path = Theme.Round(r, r.Width * 0.28f))
            using (var br = Theme.Grad(r, 45f))
                g.FillPath(br, path);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = r.Width;
            using (var wb = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.White, Math.Max(1f, s * 0.07f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (k == MediaKind.Audio)
                {
                    float hw = s * 0.13f, hh = s * 0.1f;
                    g.FillEllipse(wb, cx - s * 0.2f - hw, cy + s * 0.17f - hh, hw * 2, hh * 2);
                    g.FillEllipse(wb, cx + s * 0.16f - hw, cy + s * 0.11f - hh, hw * 2, hh * 2);
                    g.DrawLines(pen, new[] { new PointF(cx - s * 0.08f, cy + s * 0.15f), new PointF(cx - s * 0.08f, cy - s * 0.24f),
                                             new PointF(cx + s * 0.28f, cy - s * 0.3f), new PointF(cx + s * 0.28f, cy + s * 0.09f) });
                }
                else
                {
                    var fr = new RectangleF(cx - s * 0.28f, cy - s * 0.22f, s * 0.56f, s * 0.44f);
                    using (var p = Theme.Round(fr, s * 0.05f)) g.DrawPath(pen, p);
                    g.FillEllipse(wb, cx + s * 0.06f, cy - s * 0.13f, s * 0.1f, s * 0.1f);
                    g.FillPolygon(wb, new[] { new PointF(fr.X + s * 0.03f, fr.Bottom - s * 0.03f), new PointF(cx - s * 0.08f, cy - s * 0.04f),
                                              new PointF(cx + s * 0.05f, cy + s * 0.1f), new PointF(cx + s * 0.13f, cy + s * 0.04f),
                                              new PointF(fr.Right - s * 0.03f, fr.Bottom - s * 0.03f) });
                }
            }
        }

        static Bitmap Render(int size, MediaKind k)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                float m = size * 0.04f;
                var r = new RectangleF(m, m, size - 2 * m, size - 2 * m);
                if (k == MediaKind.None) DrawLogo(g, r); else DrawType(g, r, k);
            }
            return b;
        }

        public static void WriteAppIco(string path) { WriteIco(path, MediaKind.None); }

        public static void WriteIco(string path, MediaKind k)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var pngs = sizes.Select(z =>
            {
                using (var b = Render(z, k))
                using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
            }).ToArray();
            using (var f = File.Create(path))
            {
                var w = new BinaryWriter(f);
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
                w.Flush();
            }
        }

        public static Icon AppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { using (var b = Render(32, MediaKind.None)) return Icon.FromHandle(b.GetHicon()); }
        }
    }
}
