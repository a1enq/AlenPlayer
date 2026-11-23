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
    // ───────────────────────────── Слоёные окна поверх видео ─────────────────────────────
    // Окно движка — чужое (GPU), поверх него можно рисовать только отдельными окнами.
    // Окна с попиксельной прозрачностью: сглаженные края, плавное появление, не забирают фокус.
    class LayeredWindow : Form
    {
        byte alpha;
        bool wantVisible;
        readonly WinTimer anim;
        public bool IsShown { get { return wantVisible; } }

        public LayeredWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            anim = new WinTimer { Interval = 15 };
            anim.Tick += (s, e) => Animate();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x08000000 | 0x80; return cp; } // LAYERED | NOACTIVATE | TOOLWINDOW
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE → MA_NOACTIVATE
            base.WndProc(ref m);
        }

        protected virtual void Draw(Graphics g) { }

        public void Place(Rectangle r)
        {
            if (r == Bounds) return;
            bool resized = r.Size != Size;
            Bounds = r;
            if (resized && Visible) Redraw();
        }

        public void Redraw()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    Draw(g);
                }
                FreeCache();
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                cacheDC = Native.CreateCompatibleDC(screen);
                Native.ReleaseDC(IntPtr.Zero, screen);
                cacheBmp = bmp.GetHbitmap(Color.FromArgb(0));
                cacheOld = Native.SelectObject(cacheDC, cacheBmp);
                cacheSize = bmp.Size;
            }
            Push();
        }

        // Последний кадр держим в памяти: при анимации меняется только прозрачность, без перерисовки
        IntPtr cacheDC, cacheBmp, cacheOld;
        Size cacheSize;

        void Push()
        {
            if (cacheDC == IntPtr.Zero || !IsHandleCreated) return;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            var size = new Native.SIZE { W = cacheSize.Width, H = cacheSize.Height };
            var src = new Native.POINT();
            var dst = new Native.POINT { X = Left, Y = Top };
            var bl = new Native.BLEND { Op = 0, Flags = 0, Alpha = alpha, Format = 1 };
            Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, cacheDC, ref src, 0, ref bl, 2);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }

        void FreeCache()
        {
            if (cacheDC == IntPtr.Zero) return;
            Native.SelectObject(cacheDC, cacheOld);
            Native.DeleteObject(cacheBmp);
            Native.DeleteDC(cacheDC);
            cacheDC = cacheBmp = cacheOld = IntPtr.Zero;
        }

        protected override void Dispose(bool disposing) { FreeCache(); base.Dispose(disposing); }

        void SetAlpha(byte a)
        {
            alpha = a;
            Push();
        }

        public void FadeTo(bool visible)
        {
            if (visible == wantVisible) return;
            wantVisible = visible;
            if (visible && !Visible)
            {
                alpha = 0;
                var h = Handle;
                Redraw();
                Show();
                Redraw(); // после Show содержимое слоёного окна нужно передать заново
            }
            anim.Start();
        }

        public void HideNow()
        {
            wantVisible = false;
            anim.Stop();
            if (Visible) Hide();
            alpha = 0;
        }

        void Animate()
        {
            int a = alpha + (wantVisible ? 40 : -22); // появление ~100 мс, исчезновение ~180 мс
            if (a >= 255) { a = 255; anim.Stop(); }
            if (a <= 0) { a = 0; anim.Stop(); Hide(); }
            if (IsHandleCreated) SetAlpha((byte)a);
        }
    }

    class ArrowButton : LayeredWindow
    {
        readonly Ico icon;
        bool hover;
        public event Action Clicked;

        public ArrowButton(Ico icon) { this.icon = icon; Cursor = Cursors.Hand; }

        protected override void Draw(Graphics g)
        {
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            using (var b = new SolidBrush(Color.FromArgb(hover ? 225 : 170, 24, 25, 32))) g.FillEllipse(b, r);
            using (var p = new Pen(Color.FromArgb(hover ? 60 : 35, 255, 255, 255))) g.DrawEllipse(p, r);
            Icons.Draw(g, icon, Width / 2f + (icon == Ico.ChevL ? -Theme.SF(1) : Theme.SF(1)), Height / 2f, Width * 0.26f, Color.White, false);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Redraw(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Redraw(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Clicked != null) Clicked();
        }
    }

    // Панель управления в стиле проигрывателя Windows
    class OsdBar : LayeredWindow
    {
        enum Hit { None, Play, Back, Fwd, Mute, Vol, Seek, Loop, List, Full, More }

        public event Action PlayClick, BackClick, FwdClick, MuteClick, LoopClick, ListClick, FullClick;
        public event Action<Point> MoreClick;
        public event Action<double, bool> SeekTo;
        public event Action<double> VolumeTo;
        public event Action<int> Wheel;

        readonly Dictionary<Hit, Rectangle> rects = new Dictionary<Hit, Rectangle>();
        Rectangle curRect, durRect;
        Hit hover, down, drag;
        int hoverX = -1;
        bool playing, muted, loop, fullscreen;
        double pos, dur, vol = 80, dragFrac;
        string curText = "00:00", durText = "00:00";

        public int BubbleH { get { return Theme.S(30); } }
        public int BarH { get { return Theme.S(50); } }
        public int TotalH { get { return BubbleH + BarH; } }
        public bool Busy { get { return drag != Hit.None; } }

        public OsdBar() { }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutItems(); }

        void LayoutItems()
        {
            rects.Clear();
            int b = Theme.S(38), cy = BubbleH + BarH / 2, x = Theme.S(8), r = Width - Theme.S(8);
            bool wide = Width > Theme.S(620);
            Func<Hit, int, Rectangle> left = (h, w) => { var rc = new Rectangle(x, cy - b / 2, w, b); rects[h] = rc; x += w; return rc; };
            Func<Hit, int, Rectangle> right = (h, w) => { var rc = new Rectangle(r - w, cy - b / 2, w, b); rects[h] = rc; r -= w; return rc; };
            left(Hit.Play, b); left(Hit.Back, b); left(Hit.Fwd, b); left(Hit.Mute, b);
            if (wide) left(Hit.Vol, Theme.S(78));
            x += Theme.S(8);
            int tw = Theme.S(dur >= 3600 ? 64 : 48);
            curRect = new Rectangle(x, cy - b / 2, tw, b); x += tw + Theme.S(10);
            right(Hit.More, b); right(Hit.Full, b);
            if (wide) right(Hit.List, b);
            right(Hit.Loop, b);
            r -= Theme.S(8);
            durRect = new Rectangle(r - tw, cy - b / 2, tw, b); r -= tw + Theme.S(10);
            rects[Hit.Seek] = new Rectangle(x, cy - b / 2, Math.Max(10, r - x), b);
        }

        // ── Состояние ──
        public void SetPlaying(bool p) { if (p != playing) { playing = p; Refresh2(); } }
        public void SetLoop(bool l) { if (l != loop) { loop = l; Refresh2(); } }
        public void SetFullscreen(bool f) { if (f != fullscreen) { fullscreen = f; Refresh2(); } }
        public void SetVolume(double v, bool m)
        {
            if (Math.Abs(v - vol) < 0.5 && m == muted) return;
            if (drag != Hit.Vol) vol = v;
            muted = m; Refresh2();
        }

        public void SetTime(double p, double d)
        {
            bool relayout = (d >= 3600) != (dur >= 3600);
            string c = Theme.Time(p), t = Theme.Time(d);
            Rectangle sr;
            int oldPx = rects.TryGetValue(Hit.Seek, out sr) && dur > 0 ? (int)(sr.Width * pos / dur) : -1;
            pos = p; dur = d;
            if (relayout) LayoutItems();
            int newPx = rects.TryGetValue(Hit.Seek, out sr) && dur > 0 ? (int)(sr.Width * pos / dur) : -1;
            if (c != curText || t != durText || oldPx != newPx || relayout) { curText = c; durText = t; if (drag != Hit.Seek) Refresh2(); }
        }

        void Refresh2() { if (Visible) Redraw(); }

        // ── Рисование ──
        protected override void Draw(Graphics g)
        {
            var bar = new RectangleF(0.5f, BubbleH + 0.5f, Width - 1, BarH - 1);
            using (var p = Theme.Round(bar, Theme.SF(8)))
            {
                using (var b = new SolidBrush(Color.FromArgb(238, 32, 33, 40))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255))) g.DrawPath(pen, p);
            }
            Color ic = Color.FromArgb(240, 240, 245);
            foreach (var kv in rects)
            {
                if (kv.Key == Hit.Seek || kv.Key == Hit.Vol) continue;
                var rc = kv.Value;
                if (hover == kv.Key)
                    using (var hp = Theme.Round(new RectangleF(rc.X + 2, rc.Y + 2, rc.Width - 4, rc.Height - 4), Theme.SF(6)))
                    using (var hb = new SolidBrush(Color.FromArgb(down == kv.Key ? 14 : 24, 255, 255, 255))) g.FillPath(hb, hp);
                float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height / 2f, ir = Theme.SF(9);
                switch (kv.Key)
                {
                    case Hit.Play: Icons.Draw(g, playing ? Ico.Pause : Ico.Play, cx + (playing ? 0 : Theme.SF(1)), cy, Theme.SF(9.5f), ic, false); break;
                    case Hit.Back: Icons.Draw(g, Ico.Back10, cx, cy, Theme.SF(11), ic, false); break;
                    case Hit.Fwd: Icons.Draw(g, Ico.Fwd30, cx, cy, Theme.SF(11), ic, false); break;
                    case Hit.Mute: Icons.Draw(g, muted || vol <= 0 ? Ico.Mute : vol < 50 ? Ico.VolLow : Ico.Vol, cx, cy, ir, ic, false); break;
                    case Hit.Loop:
                        Icons.Draw(g, Ico.Loop, cx, cy, ir, loop ? Theme.A2 : ic, false);
                        if (loop) using (var db = new SolidBrush(Theme.A2)) g.FillEllipse(db, cx - Theme.SF(2), rc.Bottom - Theme.SF(7), Theme.SF(4), Theme.SF(4));
                        break;
                    case Hit.List: Icons.Draw(g, Ico.List, cx, cy, ir * 0.95f, ic, false); break;
                    case Hit.Full: Icons.Draw(g, fullscreen ? Ico.Unfull : Ico.Full, cx, cy, ir, ic, false); break;
                    case Hit.More: Icons.Draw(g, Ico.More, cx, cy, ir * 1.1f, ic, false); break;
                }
            }
            using (var tb = new SolidBrush(Color.FromArgb(230, 230, 236)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(curText, Theme.OsdFont, tb, curRect, sf);
                g.DrawString(durText, Theme.OsdFont, tb, durRect, sf);
            }
            Rectangle vr;
            if (rects.TryGetValue(Hit.Vol, out vr)) DrawTrack(g, vr, vol / 100.0, hover == Hit.Vol || drag == Hit.Vol, false);
            var sr = rects[Hit.Seek];
            double frac = drag == Hit.Seek ? dragFrac : (dur > 0 ? pos / dur : 0);
            DrawTrack(g, sr, frac, true, hover == Hit.Seek || drag == Hit.Seek);
            if ((hover == Hit.Seek || drag == Hit.Seek) && dur > 0 && hoverX >= 0)
            {
                double f = drag == Hit.Seek ? dragFrac : Frac(sr, hoverX);
                string s = Theme.Time(f * dur);
                var sz = g.MeasureString(s, Theme.OsdFont);
                float bw = sz.Width + Theme.SF(14), bh = Theme.SF(24);
                float bx = Math.Max(0, Math.Min(Width - bw, (float)(sr.X + Theme.SF(8) + f * (sr.Width - Theme.SF(16))) - bw / 2));
                var br = new RectangleF(bx, BubbleH - bh - Theme.SF(4), bw, bh);
                using (var p = Theme.Round(br, Theme.SF(6)))
                {
                    using (var b = new SolidBrush(Color.FromArgb(238, 32, 33, 40))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255))) g.DrawPath(pen, p);
                }
                using (var tb = new SolidBrush(Color.White))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(s, Theme.OsdFont, tb, br, sf);
            }
        }

        void DrawTrack(Graphics g, Rectangle rc, double frac, bool thumb, bool hot)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            float pad = Theme.SF(8), th = Theme.SF(hot ? 5 : 4), cy = rc.Y + rc.Height / 2f;
            var tr = new RectangleF(rc.X + pad, cy - th / 2, rc.Width - 2 * pad, th);
            using (var p = Theme.Round(tr, th / 2)) using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(b, p);
            float fw = (float)(tr.Width * frac);
            if (fw >= 1)
                using (var p = Theme.Round(new RectangleF(tr.X, tr.Y, fw, th), th / 2))
                using (var b = Theme.Grad(new RectangleF(tr.X - 1, tr.Y, tr.Width + 2, th), 0f)) g.FillPath(b, p);
            if (thumb || hover == Hit.Vol || drag == Hit.Vol)
            {
                float tx = tr.X + fw, ro = Theme.SF(hot ? 9 : 8), ri = Theme.SF(hot ? 5.5f : 4.5f);
                using (var b = new SolidBrush(Color.FromArgb(255, 32, 33, 40))) g.FillEllipse(b, tx - ro, cy - ro, ro * 2, ro * 2);
                using (var b = new SolidBrush(Theme.A2)) g.FillEllipse(b, tx - ri, cy - ri, ri * 2, ri * 2);
            }
        }

        static double Frac(Rectangle rc, int x)
        {
            float pad = Theme.SF(8);
            return Math.Max(0, Math.Min(1, (x - rc.X - pad) / Math.Max(1, rc.Width - 2 * pad)));
        }

        // ── Мышь ──
        Hit HitTest(Point p)
        {
            foreach (var kv in rects) if (kv.Value.Contains(p)) return kv.Key;
            return Hit.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = drag != Hit.None ? drag : HitTest(e.Location);
            bool changed = h != hover || (h == Hit.Seek && e.X != hoverX);
            hover = h; hoverX = e.X;
            if (drag == Hit.Seek) { dragFrac = Frac(rects[Hit.Seek], e.X); if (SeekTo != null) SeekTo(dragFrac, false); changed = true; }
            else if (drag == Hit.Vol) { vol = Frac(rects[Hit.Vol], e.X) * 100; if (VolumeTo != null) VolumeTo(vol); changed = true; }
            Cursor = h == Hit.None ? Cursors.Default : Cursors.Hand;
            if (changed) Redraw();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            down = HitTest(e.Location);
            if (down == Hit.Seek && dur > 0)
            {
                drag = Hit.Seek; Capture = true;
                dragFrac = Frac(rects[Hit.Seek], e.X);
                if (SeekTo != null) SeekTo(dragFrac, false);
            }
            else if (down == Hit.Vol)
            {
                drag = Hit.Vol; Capture = true;
                vol = Frac(rects[Hit.Vol], e.X) * 100;
                if (VolumeTo != null) VolumeTo(vol);
            }
            Redraw();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (drag == Hit.Seek)
            {
                pos = dragFrac * dur;
                if (SeekTo != null) SeekTo(dragFrac, true);
            }
            else if (drag == Hit.None && down != Hit.None && down == HitTest(e.Location))
            {
                Action a = null;
                switch (down)
                {
                    case Hit.Play: a = PlayClick; break;
                    case Hit.Back: a = BackClick; break;
                    case Hit.Fwd: a = FwdClick; break;
                    case Hit.Mute: a = MuteClick; break;
                    case Hit.Loop: a = LoopClick; break;
                    case Hit.List: a = ListClick; break;
                    case Hit.Full: a = FullClick; break;
                    case Hit.More:
                        if (MoreClick != null) { var rc = rects[Hit.More]; MoreClick(PointToScreen(new Point(rc.Right, rc.Top))); }
                        break;
                }
                if (a != null) a();
            }
            drag = Hit.None; down = Hit.None; Capture = false;
            hover = HitTest(e.Location);
            Redraw();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (drag != Hit.None) return;
            hover = Hit.None; hoverX = -1; down = Hit.None;
            Redraw();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Wheel != null) Wheel(e.Delta > 0 ? 1 : -1);
        }
    }
}
