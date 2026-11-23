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
    // ───────────────────────────── Контролы окна ─────────────────────────────
    class IconButton : Control
    {
        public Ico Icon;
        public bool Danger, Thin;
        public float IconR;
        bool hover, down;

        public IconButton(Ico icon, int w, int h, Color bg)
        {
            Icon = icon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Size = new Size(w, h);
            BackColor = bg;
            IconR = Math.Min(w, h) * 0.27f;
        }

        public void SetIcon(Ico i) { if (Icon != i) { Icon = i; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Color fg = hover ? Theme.Text : Theme.Icon;
            if (hover)
            {
                using (var b = new SolidBrush(Danger ? Color.FromArgb(232, 17, 35) : (down ? Theme.Surface2 : Theme.Hover)))
                    g.FillRectangle(b, ClientRectangle);
                if (Danger) fg = Color.White;
            }
            Icons.Draw(g, Icon, Width / 2f, Height / 2f, IconR, fg, Thin);
        }
    }

    class VideoHost : Control
    {
        public VideoHost()
        {
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.Selectable, true);
        }
    }

    class WelcomeView : Control
    {
        public WelcomeView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = Width / 2f, cy = Height / 2f - Theme.SF(48);

            float gr = Theme.SF(240);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
                using (var pb = new PathGradientBrush(path))
                {
                    pb.CenterColor = Color.FromArgb(48, Theme.A1);
                    pb.SurroundColors = new[] { Color.FromArgb(0, Theme.A1) };
                    g.FillPath(pb, path);
                }
            }

            float r = Theme.SF(52);
            var rr = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (var lb = Theme.Grad(rr, 45f))
            {
                using (var pen = new Pen(lb, Theme.SF(3.5f))) g.DrawEllipse(pen, rr);
                float s = r * 0.42f;
                var pts = new[] { new PointF(cx - s * 0.65f, cy - s), new PointF(cx + s * 1.1f, cy), new PointF(cx - s * 0.65f, cy + s) };
                g.FillPolygon(lb, pts);
                using (var pen = new Pen(lb, Theme.SF(4)) { LineJoin = LineJoin.Round }) g.DrawPolygon(pen, pts);
            }

            int y = (int)(cy + r + Theme.SF(24));
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, L.T("Перетащите файл сюда", "Drop a file here"), Theme.Big, new Rectangle(0, y, Width, Theme.S(40)), Theme.Text, flags);
            y += Theme.S(42);
            TextRenderer.DrawText(g, L.T("или нажмите в любом месте, чтобы открыть   ·   Ctrl+O", "or click anywhere to open   ·   Ctrl+O"), Theme.UI,
                new Rectangle(0, y, Width, Theme.S(24)), Theme.Dim, flags);
            y += Theme.S(44);

            string[][] chips =
            {
                new[] { L.T("Видео", "Video"), "MP4 · MKV · AVI · MOV · WEBM" },
                new[] { L.T("Аудио", "Audio"), "MP3 · FLAC · WAV · OGG · M4A" },
                new[] { L.T("Фото", "Photo"), "JPG · PNG · WEBP · HEIC · GIF" },
            };
            int gap = Theme.S(10), ph = Theme.S(30), padX = Theme.S(14);
            var widths = chips.Select(c => TextRenderer.MeasureText(c[0], Theme.UIBold).Width + Theme.S(8) +
                                           TextRenderer.MeasureText(c[1], Theme.Small).Width + padX * 2).ToArray();
            int total = widths.Sum() + gap * (chips.Length - 1);
            bool row = total < Width - Theme.S(40);
            int x = row ? (Width - total) / 2 : 0;
            for (int i = 0; i < chips.Length; i++)
            {
                int cw = widths[i];
                int px = row ? x : (Width - cw) / 2;
                var rc = new Rectangle(px, y, cw, ph);
                using (var p = Theme.Round(rc, ph / 2f))
                {
                    using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, p);
                    using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, p);
                }
                int lw = TextRenderer.MeasureText(chips[i][0], Theme.UIBold).Width;
                var vf = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, chips[i][0], Theme.UIBold, new Rectangle(px + padX, y, lw + 2, ph), Theme.Text, Theme.Surface, vf);
                TextRenderer.DrawText(g, chips[i][1], Theme.Small, new Rectangle(px + padX + lw + Theme.S(8), y, cw, ph), Theme.Dim, Theme.Surface, vf);
                if (row) x += cw + gap; else y += ph + gap;
            }
        }
    }

    class TitleBar : Panel
    {
        public readonly IconButton Open, More, Min, Max, Close;
        public event Action TitleDrag, TitleDoubleClick;
        string text = "AlenPlayer";

        public TitleBar()
        {
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            Height = Theme.S(38);
            int bw = Theme.S(46), bh = Theme.S(38);
            Open = new IconButton(Ico.Open, Theme.S(40), bh, Theme.Bg) { IconR = Theme.SF(8.5f) };
            More = new IconButton(Ico.More, Theme.S(40), bh, Theme.Bg) { IconR = Theme.SF(10) };
            Min = new IconButton(Ico.Min, bw, bh, Theme.Bg) { Thin = true, IconR = Theme.SF(10) };
            Max = new IconButton(Ico.Max, bw, bh, Theme.Bg) { Thin = true, IconR = Theme.SF(10) };
            Close = new IconButton(Ico.Close, bw, bh, Theme.Bg) { Thin = true, Danger = true, IconR = Theme.SF(10) };
            Controls.AddRange(new Control[] { Open, More, Min, Max, Close });
        }

        public string Caption { set { text = string.IsNullOrEmpty(value) ? "AlenPlayer" : value; Invalidate(); } }
        public void SetMaximized(bool m) { Max.SetIcon(m ? Ico.Restore : Ico.Max); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Close == null) return;
            Close.Location = new Point(Width - Close.Width, 0);
            Max.Location = new Point(Close.Left - Max.Width, 0);
            Min.Location = new Point(Max.Left - Min.Width, 0);
            More.Location = new Point(Min.Left - More.Width - Theme.S(6), 0);
            Open.Location = new Point(More.Left - Open.Width, 0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks == 2) { if (TitleDoubleClick != null) TitleDoubleClick(); }
            else if (TitleDrag != null) TitleDrag();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int ls = Theme.S(18), lx = Theme.S(12);
            IconMaker.DrawLogo(g, new RectangleF(lx, (Height - ls) / 2f, ls, ls));
            int x = lx + ls + Theme.S(10);
            TextRenderer.DrawText(g, text, Theme.UI, new Rectangle(x, 0, Math.Max(10, Open.Left - x - Theme.S(10)), Height),
                Theme.Text, Theme.Bg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    class PlaylistPanel : Panel
    {
        public readonly ListBox Box;
        public event Action<int> ItemActivated;
        List<string> items = new List<string>();
        int current = -1, hoverIndex = -1;

        public PlaylistPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            Padding = new Padding(1, Theme.S(46), 0, 0);
            Box = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Theme.S(40), BackColor = Theme.Surface, ForeColor = Theme.Text, IntegralHeight = false,
            };
            Box.HandleCreated += (s, e) => { try { Native.SetWindowTheme(Box.Handle, "DarkMode_Explorer", null); } catch { } };
            Box.DrawItem += DrawItem;
            Box.MouseClick += (s, e) =>
            {
                int i = Box.IndexFromPoint(e.Location);
                if (i >= 0 && ItemActivated != null) ItemActivated(i);
            };
            Box.MouseMove += (s, e) =>
            {
                int i = Box.IndexFromPoint(e.Location);
                if (i != hoverIndex) { InvalidateItem(hoverIndex); hoverIndex = i; InvalidateItem(i); }
            };
            Box.MouseLeave += (s, e) => { InvalidateItem(hoverIndex); hoverIndex = -1; };
            Controls.Add(Box);
        }

        void InvalidateItem(int i) { if (i >= 0 && i < Box.Items.Count) Box.Invalidate(Box.GetItemRectangle(i)); }

        public void SetItems(List<string> list)
        {
            items = new List<string>(list);
            Box.BeginUpdate();
            Box.Items.Clear();
            foreach (var f in items) Box.Items.Add(Path.GetFileName(f));
            Box.EndUpdate();
            Invalidate();
        }

        public void SetCurrent(int i)
        {
            int old = current;
            current = i;
            InvalidateItem(old); InvalidateItem(i);
            if (i >= 0 && i < Box.Items.Count)
            {
                int vis = Math.Max(1, Box.ClientSize.Height / Box.ItemHeight);
                if (i < Box.TopIndex || i >= Box.TopIndex + vis) Box.TopIndex = Math.Max(0, i - vis / 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, 0, 0, Height);
            var vc = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var hr = new Rectangle(Theme.S(16), 0, Width - Theme.S(32), Theme.S(46));
            TextRenderer.DrawText(g, L.T("ПЛЕЙЛИСТ", "PLAYLIST"), Theme.Tiny, hr, Theme.Dim, Theme.Surface, vc | TextFormatFlags.Left);
            TextRenderer.DrawText(g, L.Files(items.Count), Theme.Small, hr, Theme.Dim, Theme.Surface, vc | TextFormatFlags.Right);
        }


        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= items.Count) return;
            var g = e.Graphics;
            var b = e.Bounds;
            bool cur = e.Index == current;
            Color bg = cur ? Theme.Surface2 : (e.Index == hoverIndex ? Theme.Hover : Theme.Surface);
            using (var br = new SolidBrush(bg)) g.FillRectangle(br, b);
            if (cur)
                using (var lb = Theme.Grad(new RectangleF(b.X, b.Y, Theme.S(3), b.Height), 90f))
                    g.FillRectangle(lb, b.X, b.Y, Theme.S(3), b.Height);
            var vc = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, (e.Index + 1).ToString("00"), Theme.Small, new Rectangle(b.X + Theme.S(14), b.Y, Theme.S(26), b.Height),
                cur ? Theme.A2 : Theme.Dim, bg, vc | TextFormatFlags.Left);
            string path = items[e.Index];
            string ext = (Path.GetExtension(path) ?? "").TrimStart('.').ToUpperInvariant();
            int extW = Theme.S(42);
            TextRenderer.DrawText(g, Path.GetFileNameWithoutExtension(path), cur ? Theme.UIBold : Theme.UI,
                new Rectangle(b.X + Theme.S(44), b.Y, b.Width - Theme.S(44) - extW - Theme.S(10), b.Height),
                cur ? Color.White : Theme.Text, bg, vc | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, ext, Theme.Tiny, new Rectangle(b.Right - extW - Theme.S(10), b.Y, extW, b.Height),
                Theme.Dim, bg, vc | TextFormatFlags.Right);
        }
    }
}
