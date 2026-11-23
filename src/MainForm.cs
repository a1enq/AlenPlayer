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
    // ───────────────────────────── Главное окно ─────────────────────────────
    class MainForm : Form
    {
        readonly string[] startArgs;
        Player mpv;
        readonly VideoHost video;
        readonly WelcomeView welcome;
        readonly TitleBar title;
        readonly PlaylistPanel plist;
        readonly Panel content;
        readonly OsdBar osd;
        readonly ArrowButton leftArrow, rightArrow;
        readonly WinTimer uiTimer, slideTimer;
        readonly Settings settings;
        readonly Cursor blankCursor;
        readonly Native.EnumProc enumCb;

        List<string> files = new List<string>();
        int index = -1;
        string currentPath, noLavfiPath;
        MediaKind kind = MediaKind.None;
        double pos, dur, volume = 80, speed = 1, imgW, imgH, zoom, panX, panY;
        int rotate, lastMove, kickUntil, lastPoll;
        bool paused, muted, loop, isImageTrack, eof, fullscreen, maximized, loadedOnce, plistWasVisible, cursorHidden, panning, zoneClick;
        Rectangle normalBounds;
        Point lastMouse, panStart;
        double panStartX, panStartY;
        IntPtr mpvWnd;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        int Grip { get { return Theme.S(6); } }
        bool ImageMode { get { return kind == MediaKind.Image || (kind != MediaKind.Audio && isImageTrack); } }

        public MainForm(string[] args)
        {
            startArgs = args;
            Text = "AlenPlayer";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(Theme.S(560), Theme.S(340));
            Padding = new Padding(Theme.S(4));
            Icon = IconMaker.AppIcon();
            using (var bmp = new Bitmap(32, 32)) blankCursor = new Cursor(bmp.GetHicon());
            enumCb = OnEnumChild;

            settings = Settings.Load();
            volume = settings.Volume;
            loop = settings.Loop;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var saved = new Rectangle(settings.X, settings.Y, settings.W, settings.H);
            if (settings.W >= MinimumSize.Width && settings.H >= MinimumSize.Height && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved)))
                Bounds = saved;
            else
            {
                var sz = new Size(Math.Min(Theme.S(1120), wa.Width - 80), Math.Min(Theme.S(680), wa.Height - 80));
                Bounds = new Rectangle(wa.X + (wa.Width - sz.Width) / 2, wa.Y + (wa.Height - sz.Height) / 2, sz.Width, sz.Height);
            }
            normalBounds = Bounds;

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            video = new VideoHost { Dock = DockStyle.Fill };
            welcome = new WelcomeView { Dock = DockStyle.Fill };
            content.Controls.Add(video);
            content.Controls.Add(welcome);
            welcome.BringToFront();
            plist = new PlaylistPanel { Dock = DockStyle.Right, Width = Theme.S(300), Visible = false };
            title = new TitleBar { Dock = DockStyle.Top };
            Controls.Add(content);
            Controls.Add(plist);
            Controls.Add(title);

            osd = new OsdBar();
            leftArrow = new ArrowButton(Ico.ChevL);
            rightArrow = new ArrowButton(Ico.ChevR);
            osd.SetLoop(loop);

            // Заголовок
            title.Min.Click += (s, e) => WindowState = FormWindowState.Minimized;
            title.Max.Click += (s, e) => ToggleMax();
            title.Close.Click += (s, e) => Close();
            title.Open.Click += (s, e) => OpenDialog();
            title.More.Click += (s, e) => ShowMenu(title.PointToScreen(new Point(title.More.Right, title.More.Bottom)), ToolStripDropDownDirection.BelowLeft);
            title.TitleDoubleClick += ToggleMax;
            title.TitleDrag += () =>
            {
                if (maximized || fullscreen) return;
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            };

            // Панель управления
            osd.PlayClick += TogglePlay;
            osd.BackClick += () => Seek(-10);
            osd.FwdClick += () => Seek(30);
            osd.MuteClick += ToggleMute;
            osd.LoopClick += ToggleLoop;
            osd.ListClick += TogglePlaylist;
            osd.FullClick += () => SetFullscreen(!fullscreen);
            osd.MoreClick += p => ShowMenu(p, ToolStripDropDownDirection.AboveLeft);
            osd.SeekTo += (f, final) =>
            {
                if (mpv == null || dur <= 0) return;
                mpv.Cmd("seek", (f * dur).ToString("0.###", Inv), final ? "absolute+exact" : "absolute+keyframes");
                if (final) { pos = f * dur; osd.SetTime(pos, dur); }
            };
            osd.VolumeTo += v => SetVolume(v);
            osd.Wheel += d => AddVolume(5 * d);
            leftArrow.Clicked += () => Step(-1);
            rightArrow.Clicked += () => Step(1);

            // Видео
            video.MouseDown += OnVideoMouseDown;
            video.MouseMove += OnVideoMouseMove;
            video.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) panning = false; };
            video.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && !zoneClick && currentPath != null && !ImageMode) TogglePlay();
                else if (e.Button == MouseButtons.Right) ShowMenu(video.PointToScreen(e.Location), ToolStripDropDownDirection.Default);
            };
            video.MouseDoubleClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || zoneClick) return;
                SetFullscreen(!fullscreen);
                if (currentPath != null && !ImageMode) TogglePlay();
            };
            video.MouseWheel += (s, e) =>
            {
                if (currentPath == null) return;
                if (ImageMode) ZoomAt(e.Location, e.Delta > 0 ? 0.25 : -0.25);
                else AddVolume(e.Delta > 0 ? 5 : -5);
            };
            welcome.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) OpenDialog();
                else ShowMenu(welcome.PointToScreen(e.Location), ToolStripDropDownDirection.Default);
            };

            plist.ItemActivated += i => { if (i >= 0 && i < files.Count) OpenFile(files[i], false); };

            foreach (var c in new Control[] { this, video, welcome, title, plist.Box })
            {
                c.AllowDrop = true;
                c.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
                c.DragDrop += (s, e) =>
                {
                    var arr = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (arr != null) BeginInvoke(new Action(() => OpenPaths(arr)));
                };
            }

            Move += (s, e) => PlaceOverlays();
            video.SizeChanged += (s, e) => { PlaceOverlays(); if (ImageMode) { ClampPan(); ApplyPan(); } };

            uiTimer = new WinTimer { Interval = 100 };
            uiTimer.Tick += (s, e) => OnTick();
            slideTimer = new WinTimer { Interval = 4000 };
            slideTimer.Tick += (s, e) => Step(1);

            if (settings.Max) Load += (s, e) => ToggleMax();
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style |= 0x20000; return cp; } // WS_MINIMIZEBOX
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetCorners(true);
            try { int c = 0x00382A28; Native.DwmSetWindowAttribute(Handle, 34, ref c, 4); } catch { }
        }

        void SetCorners(bool round)
        {
            try { int v = round ? 2 : 1; Native.DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            osd.Owner = this; leftArrow.Owner = this; rightArrow.Owner = this;
            try { InitPlayer(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("Ошибка запуска движка: ", "Failed to start the engine: ") + ex.Message, "AlenPlayer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            uiTimer.Start();
            if (startArgs.Length > 0) OpenPaths(startArgs);
        }

        void InitPlayer()
        {
            mpv = new Player(this);
            mpv.Opt("wid", video.Handle.ToInt64().ToString(Inv));
            mpv.Opt("input-vo-keyboard", "no");
            mpv.Opt("input-cursor", "no");
            mpv.Opt("cursor-autohide", "no");
            mpv.Opt("vo", "gpu-next");
            mpv.Opt("hwdec", "auto-safe");          // декодирование на видеокарте
            mpv.Opt("profile", "fast");             // лёгкие шейдеры масштабирования для видео
            mpv.Opt("keep-open", "yes");
            mpv.Opt("idle", "yes");
            mpv.Opt("force-window", "yes");
            mpv.Opt("image-display-duration", "inf");
            mpv.Opt("background", "color");          // прозрачность — цветом приложения, без «шахматки»
            mpv.Opt("background-color", Theme.BgHex);
            mpv.Opt("volume-max", "100");
            mpv.Opt("volume", volume.ToString("0", Inv));
            mpv.Opt("loop-file", loop ? "inf" : "no");
            mpv.Opt("sub-auto", "fuzzy");
            mpv.Opt("audio-file-auto", "no");
            mpv.Opt("demuxer-max-bytes", "48MiB");   // меньше ОЗУ под буфер
            mpv.Opt("demuxer-max-back-bytes", "8MiB");
            mpv.Opt("gpu-shader-cache", "yes");
            mpv.Opt("gpu-shader-cache-dir", Path.Combine(Path.GetTempPath(), "AlenPlayer-shaders"));
            mpv.Opt("osd-bar", "no");
            mpv.Opt("osd-font", "Segoe UI");
            mpv.Opt("osd-font-size", "28");
            mpv.Opt("osd-border-size", "1.5");
            mpv.Opt("osd-shadow-offset", "1");
            mpv.Opt("osd-duration", "1000");
            mpv.Opt("osd-color", "#FFFFFFFF");
            mpv.Opt("sub-font", "Segoe UI Semibold");
            mpv.Opt("screenshot-format", "png");
            mpv.Opt("screenshot-directory", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            mpv.Opt("save-position-on-quit", "no");
            mpv.Opt("resume-playback", "yes");
            mpv.Opt("watch-later-dir", Path.Combine(Settings.Dir, "resume"));
            mpv.PropertyChanged += OnProp;
            mpv.FileLoaded += OnFileLoaded;
            mpv.EndFile += OnEndFile;
            mpv.Start();
        }

        // ── Открытие файлов ──
        void OpenDialog()
        {
            using (var d = new OpenFileDialog { Filter = Formats.Filter(), Multiselect = true, Title = L.T("Открыть медиафайл", "Open media file") })
                if (d.ShowDialog(this) == DialogResult.OK) OpenPaths(d.FileNames);
        }

        void OpenPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            if (paths.Length == 1)
            {
                string p = paths[0];
                if (Directory.Exists(p))
                {
                    var list = SafeFiles(p).Where(f => Formats.Of(f) != MediaKind.None).ToList();
                    Sort(list);
                    var first = list.FirstOrDefault(f => Formats.Of(f) != MediaKind.Image) ?? list.FirstOrDefault();
                    if (first != null) OpenFile(first, true);
                    return;
                }
                if (File.Exists(p)) OpenFile(p, true);
                return;
            }
            var many = paths.Where(File.Exists).ToList();
            if (many.Count == 0) return;
            Sort(many);
            files = many;
            plist.SetItems(files);
            OpenFile(files[0], false);
        }

        static IEnumerable<string> SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir); } catch { return new string[0]; }
        }

        static void Sort(List<string> l)
        {
            l.Sort((a, b) => Native.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
        }

        void BuildPlaylist(string path)
        {
            bool img = Formats.Of(path) == MediaKind.Image;
            var list = SafeFiles(Path.GetDirectoryName(path)).Where(f =>
            {
                var k = Formats.Of(f);
                return k != MediaKind.None && (k == MediaKind.Image) == img;
            }).ToList();
            if (!list.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase))) list.Add(path);
            Sort(list);
            files = list;
            plist.SetItems(files);
        }

        static string Lavfi()
        {
            // Лёгкий визуализатор: малое разрешение, только столбцы, 1 преобразование на кадр
            const string graph = "[aid1]asplit[ao][a];[a]showcqt=s=960x540:fps=30:count=1:axis=0:sono_h=0:bar_g=2:bar_v=12:csp=bt709[vo]";
            return "lavfi-complex=%" + Encoding.UTF8.GetByteCount(graph) + "%" + graph;
        }

        void OpenFile(string path, bool rebuild)
        {
            if (mpv == null) return;
            path = Path.GetFullPath(path);
            if (rebuild) BuildPlaylist(path);
            SaveResume();
            currentPath = path;
            index = files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            kind = Formats.Of(path);
            if (kind == MediaKind.None) kind = MediaKind.Video;
            if (kind != MediaKind.Image) slideTimer.Stop();
            eof = false; isImageTrack = false; imgW = imgH = 0; pos = dur = 0;
            zoom = panX = panY = 0; rotate = 0;
            mpv.Set("video-zoom", "0"); mpv.Set("video-pan-x", "0"); mpv.Set("video-pan-y", "0"); mpv.Set("video-rotate", "0");

            var opts = new List<string>();
            if (kind == MediaKind.Audio && noLavfiPath != path) opts.Add(Lavfi());
            if (kind == MediaKind.Image)
            {
                // Фото: не увеличивать мелкие, качественное уменьшение больших
                opts.Add("video-unscaled=downscale-big");
                opts.Add("dscale=mitchell");
                opts.Add("scale=spline36");
                opts.Add("correct-downscaling=yes");
                opts.Add("linear-downscaling=yes");
                string e = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                if (e == ".gif" || e == ".webp" || e == ".apng" || e == ".png") opts.Add("loop-file=inf");
            }
            mpv.Cmd("loadfile", path, "replace", "-1", string.Join(",", opts));
            if (kind != MediaKind.Image) mpv.Set("pause", "no");

            welcome.Visible = false;
            Text = Path.GetFileName(path);
            title.Caption = Path.GetFileName(path);
            plist.SetCurrent(index);
            osd.SetTime(0, 0);
            if (kind == MediaKind.Image) osd.HideNow();
            UpdatePlayIcon();
            Kick();
        }

        void Step(int d)
        {
            if (files.Count == 0) return;
            int i = index + d;
            if (i < 0 || i >= files.Count)
            {
                if (kind != MediaKind.Image) return;
                i = (i + files.Count) % files.Count;
            }
            OpenFile(files[i], false);
        }

        bool ShouldSaveResume()
        {
            return currentPath != null && kind != MediaKind.Image && dur > 300 && !eof && pos > 30 && pos < dur - 30;
        }

        void SaveResume()
        {
            if (currentPath == null || mpv == null) return;
            if (ShouldSaveResume()) mpv.Cmd("write-watch-later-config");
            else if (kind != MediaKind.Image && dur > 300) mpv.Cmd("delete-watch-later-config");
        }

        // ── События движка ──
        void OnProp(string n, object v)
        {
            switch (n)
            {
                case "time-pos": if (v is double) { pos = (double)v; osd.SetTime(pos, dur); } break;
                case "duration": dur = v is double ? (double)v : 0; osd.SetTime(pos, dur); break;
                case "pause": paused = v is bool && (bool)v; UpdatePlayIcon(); UpdateAwake(); if (mpv != null) mpv.GetAsync("time-pos"); break;
                case "volume":
                    if (v is double) { volume = (double)v; UpdateVolIcon(); }
                    break;
                case "mute": muted = v is bool && (bool)v; UpdateVolIcon(); break;
                case "speed": if (v is double) speed = (double)v; break;
                case "eof-reached":
                    eof = v is bool && (bool)v;
                    if (eof && kind != MediaKind.Image && index >= 0 && index < files.Count - 1)
                        BeginInvoke(new Action(() => Step(1)));
                    if (eof && dur > 0) { pos = dur; osd.SetTime(pos, dur); }
                    UpdatePlayIcon(); UpdateAwake();
                    break;
                case "current-tracks/video/image":
                    isImageTrack = v is bool && (bool)v;
                    if (ImageMode) osd.HideNow();
                    UpdatePlayIcon();
                    break;
            }
        }

        void OnFileLoaded()
        {
            if (mpv == null || currentPath == null) return;
            imgW = mpv.GetD("width", 0); imgH = mpv.GetD("height", 0);
            mpv.GetAsync("time-pos");
            if (!loadedOnce) { loadedOnce = true; FitToVideo(); }
        }

        void OnEndFile(int reason, int error)
        {
            if (reason != 4 || currentPath == null) return; // 4 = ошибка
            if (kind == MediaKind.Audio && noLavfiPath != currentPath)
            {
                noLavfiPath = currentPath; // визуализатор не подошёл — играем без него
                OpenFile(currentPath, false);
                return;
            }
            Osd(L.T("Не удалось открыть файл", "Cannot open this file"), 2500);
        }

        void FitToVideo()
        {
            if (maximized || fullscreen || imgW <= 0 || imgH <= 0 || kind == MediaKind.Audio) return;
            var wa = Screen.FromControl(this).WorkingArea;
            int chromeW = Padding.Horizontal + (plist.Visible ? plist.Width : 0);
            int chromeH = Padding.Vertical + title.Height;
            double maxW = wa.Width * 0.8 - chromeW, maxH = wa.Height * 0.8 - chromeH;
            double s = Math.Min(1, Math.Min(maxW / imgW, maxH / imgH));
            int w = Math.Max(MinimumSize.Width, (int)(imgW * s) + chromeW);
            int h = Math.Max(MinimumSize.Height, (int)(imgH * s) + chromeH);
            Bounds = normalBounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        }

        // ── Управление ──
        void Osd(string text, int ms = 1000) { if (mpv != null) mpv.Cmd("show-text", text, ms.ToString(Inv)); }
        void Kick() { kickUntil = Environment.TickCount + 2500; }

        void TogglePlay()
        {
            if (mpv == null || currentPath == null) { OpenDialog(); return; }
            if (ImageMode)
            {
                if (slideTimer.Enabled) { slideTimer.Stop(); Osd(L.T("Слайд-шоу остановлено", "Slideshow stopped")); }
                else if (files.Count > 1) { slideTimer.Start(); Osd(L.T("Слайд-шоу", "Slideshow")); }
                return;
            }
            if (eof) { mpv.Cmd("seek", "0", "absolute"); mpv.Set("pause", "no"); }
            else mpv.Cmd("cycle", "pause");
            Kick();
        }

        void Seek(double s)
        {
            if (mpv == null || currentPath == null || ImageMode) return;
            mpv.Cmd("seek", s.ToString(Inv), "relative+exact");
            pos = Math.Max(0, Math.Min(dur > 0 ? dur : double.MaxValue, pos + s));
            osd.SetTime(pos, dur);
            Kick();
        }

        void SetVolume(double v)
        {
            if (mpv == null) return;
            v = Math.Max(0, Math.Min(100, Math.Round(v)));
            volume = v;
            mpv.Set("volume", v);
            if (muted && v > 0) mpv.Set("mute", "no");
            UpdateVolIcon();
        }

        void AddVolume(double d)
        {
            SetVolume(volume + d);
            if (ImageMode || osd.Visible) return;
            Osd(L.T("Громкость  ", "Volume  ") + volume + "%", 700);
        }

        void ToggleMute()
        {
            if (mpv == null) return;
            mpv.Cmd("cycle", "mute");
            Kick();
        }

        void ToggleLoop()
        {
            loop = !loop;
            if (mpv != null) mpv.Set("loop-file", loop ? "inf" : "no");
            osd.SetLoop(loop);
        }

        void AddSpeed(double d)
        {
            if (mpv == null || currentPath == null) return;
            double s = Math.Max(0.25, Math.Min(4, Math.Round((speed + d) * 100) / 100));
            if (d == 0) s = 1;
            mpv.Set("speed", s);
            Osd(L.T("Скорость  ", "Speed  ") + s.ToString("0.##", Inv) + "×");
        }

        void Rotate()
        {
            if (mpv == null || currentPath == null) return;
            rotate = (rotate + 90) % 360;
            zoom = panX = panY = 0;
            mpv.Set("video-rotate", rotate.ToString(Inv));
            mpv.Set("video-zoom", "0");
            ApplyPan();
        }

        void ResetZoom()
        {
            if (mpv == null) return;
            zoom = panX = panY = 0;
            mpv.Set("video-zoom", "0");
            ApplyPan();
        }

        void FitScale(out double vw, out double vh, out double fit)
        {
            vw = imgW; vh = imgH;
            if (rotate % 180 != 0) { double t = vw; vw = vh; vh = t; }
            double pw = Math.Max(1, video.ClientSize.Width), ph = Math.Max(1, video.ClientSize.Height);
            fit = vw > 0 && vh > 0 ? Math.Min(pw / vw, ph / vh) : 1;
            if (ImageMode) fit = Math.Min(fit, 1);
        }

        // Картинка не может уехать за рамки окна; если она меньше окна — стоит по центру
        void ClampPan()
        {
            double vw, vh, fit;
            FitScale(out vw, out vh, out fit);
            if (vw <= 0) { panX = panY = 0; return; }
            double d = fit * Math.Pow(2, zoom), dw = vw * d, dh = vh * d;
            double pw = video.ClientSize.Width, ph = video.ClientSize.Height;
            double mx = dw > pw ? (dw - pw) / (2 * dw) : 0, my = dh > ph ? (dh - ph) / (2 * dh) : 0;
            panX = Math.Max(-mx, Math.Min(mx, panX));
            panY = Math.Max(-my, Math.Min(my, panY));
        }

        void ApplyPan()
        {
            if (mpv == null) return;
            mpv.Set("video-pan-x", panX); mpv.Set("video-pan-y", panY);
        }

        // Масштаб к точке под курсором: меньше «по размеру окна» нельзя, больше — до 800% от реального размера
        void ZoomAt(Point p, double delta)
        {
            if (mpv == null || imgW <= 0) return;
            double vw, vh, fit;
            FitScale(out vw, out vh, out fit);
            double maxZ = Math.Max(1, Math.Log(8 / fit, 2));
            double nz = Math.Max(0, Math.Min(maxZ, zoom + delta));
            if (Math.Abs(nz - zoom) < 1e-9) return;
            double d0 = fit * Math.Pow(2, zoom), d1 = fit * Math.Pow(2, nz);
            double mx = p.X - video.ClientSize.Width / 2.0, my = p.Y - video.ClientSize.Height / 2.0;
            double ux = mx / (vw * d0) - panX, uy = my / (vh * d0) - panY;
            panX = mx / (vw * d1) - ux; panY = my / (vh * d1) - uy;
            zoom = nz;
            if (zoom < 1e-6) { zoom = 0; panX = panY = 0; }
            ClampPan();
            mpv.Set("video-zoom", zoom);
            ApplyPan();
            Osd(Math.Round(d1 * 100) + "%", 600);
        }

        int ZoneWidth { get { return Math.Max(Theme.S(70), Math.Min(Theme.S(200), (int)(video.ClientSize.Width * 0.16))); } }

        // Клик у левого/правого края кадра — листание (если фото увеличено — только по самой стрелке)
        int ZoneAt(Point p)
        {
            if (currentPath == null || files.Count < 2) return 0;
            if (ImageMode && zoom > 1e-6) return 0;
            if (p.X < ZoneWidth) return -1;
            if (p.X >= video.ClientSize.Width - ZoneWidth) return 1;
            return 0;
        }

        void OnVideoMouseDown(object s, MouseEventArgs e)
        {
            video.Focus();
            zoneClick = false;
            if (e.Button != MouseButtons.Left) return;
            int z = ZoneAt(e.Location);
            if (z != 0) { zoneClick = true; Step(z); return; }
            if (ImageMode && currentPath != null && zoom > 1e-6)
            {
                panning = true; panStart = e.Location; panStartX = panX; panStartY = panY;
                video.Cursor = Cursors.SizeAll;
            }
        }

        void OnVideoMouseMove(object s, MouseEventArgs e)
        {
            if (!panning) { if (video.Cursor == Cursors.SizeAll) video.Cursor = Cursors.Default; return; }
            if (mpv == null || imgW <= 0) return;
            double vw, vh, fit;
            FitScale(out vw, out vh, out fit);
            double d = fit * Math.Pow(2, zoom);
            panX = panStartX + (e.X - panStart.X) / (vw * d);
            panY = panStartY + (e.Y - panStart.Y) / (vh * d);
            ClampPan();
            ApplyPan();
        }

        void UpdatePlayIcon()
        {
            osd.SetPlaying(currentPath != null && !paused && !eof);
        }

        void UpdateVolIcon() { osd.SetVolume(volume, muted); }

        void UpdateAwake()
        {
            bool playing = currentPath != null && !paused && !eof && kind != MediaKind.Image;
            uint flags = 0x80000000; // ES_CONTINUOUS
            if (playing) flags |= kind == MediaKind.Audio ? 0x1u : 0x3u;
            Native.SetThreadExecutionState(flags);
        }

        void TogglePlaylist()
        {
            if (fullscreen) return;
            plist.Visible = !plist.Visible;
            if (plist.Visible) plist.SetCurrent(index);
        }

        // ── Окно ──
        void ToggleMax()
        {
            if (fullscreen) return;
            if (!maximized)
            {
                normalBounds = Bounds;
                maximized = true;
                Padding = Padding.Empty;
                Bounds = Screen.FromControl(this).WorkingArea;
            }
            else
            {
                maximized = false;
                Padding = new Padding(Theme.S(4));
                Bounds = normalBounds;
            }
            title.SetMaximized(maximized);
            SetCorners(!maximized);
            PlaceOverlays();
        }

        void SetFullscreen(bool on)
        {
            if (on == fullscreen) return;
            fullscreen = on;
            SuspendLayout();
            if (on)
            {
                if (!maximized) normalBounds = Bounds;
                title.Visible = false;
                plistWasVisible = plist.Visible;
                plist.Visible = false;
                Padding = Padding.Empty;
                Bounds = Screen.FromControl(this).Bounds;
                SetCorners(false);
            }
            else
            {
                title.Visible = true;
                plist.Visible = plistWasVisible;
                Padding = maximized ? Padding.Empty : new Padding(Theme.S(4));
                Bounds = maximized ? Screen.FromControl(this).WorkingArea : normalBounds;
                SetCorners(!maximized);
                SetCursorHidden(false);
            }
            ResumeLayout(true);
            osd.SetFullscreen(fullscreen);
            PlaceOverlays();
            Kick();
            Activate();
        }

        void PlaceOverlays()
        {
            if (!IsHandleCreated || osd == null) return;
            var r = video.RectangleToScreen(video.ClientRectangle);
            int m = Theme.S(14), w = Math.Max(Theme.S(320), r.Width - 2 * m);
            osd.Place(new Rectangle(r.X + (r.Width - w) / 2, r.Bottom - m - osd.TotalH, w, osd.TotalH));
            int a = Theme.S(52), ay = r.Y + (r.Height - a) / 2;
            leftArrow.Place(new Rectangle(r.X + Theme.S(16), ay, a, a));
            rightArrow.Place(new Rectangle(r.Right - Theme.S(16) - a, ay, a, a));
        }

        void OnTick()
        {
            if (WindowState == FormWindowState.Minimized || !Visible) return;
            DisableMpvWindow();
            int now = Environment.TickCount;
            Point p = Cursor.Position;
            var vr = video.RectangleToScreen(video.ClientRectangle);
            bool inVideo = vr.Contains(p);
            if (p != lastMouse) { lastMouse = p; if (inVideo) lastMove = now; }
            bool recent = now - lastMove < 2500 && inVideo;
            bool overBar = osd.Visible && osd.Bounds.Contains(p);
            bool media = mpv != null && currentPath != null;

            // Панель: видна на паузе, при движении мыши и действиях; во время игры плавно исчезает
            bool barWanted = media && !ImageMode && (paused || eof || kind == MediaKind.Audio || overBar || osd.Busy || recent || now - kickUntil < 0);
            if (barWanted && !osd.IsShown) { PlaceOverlays(); lastPoll = 0; }
            osd.FadeTo(barWanted);

            // Стрелки листания у краёв
            bool multi = media && files.Count > 1;
            int zw = ZoneWidth;
            bool inL = multi && ((recent && p.X < vr.X + zw) || (leftArrow.Visible && leftArrow.Bounds.Contains(p)));
            bool inR = multi && ((recent && p.X >= vr.Right - zw) || (rightArrow.Visible && rightArrow.Bounds.Contains(p)));
            if ((inL && !leftArrow.IsShown) || (inR && !rightArrow.IsShown)) PlaceOverlays();
            leftArrow.FadeTo(inL && inVideo);
            rightArrow.FadeTo(inR && inVideo);

            // Время опрашиваем, только пока панель видна (без лишних событий на каждый кадр)
            if (osd.IsShown && media && now - lastPoll >= 250) { lastPoll = now; mpv.GetAsync("time-pos"); }

            SetCursorHidden(fullscreen && inVideo && !barWanted && !inL && !inR && ActiveForm == this);
        }

        void SetCursorHidden(bool h)
        {
            if (h == cursorHidden) return;
            cursorHidden = h;
            video.Cursor = h ? blankCursor : Cursors.Default;
            if (h) Cursor.Hide(); else Cursor.Show();
        }

        // Окно движка отключаем от ввода — тогда мышь и клавиатура приходят в наше окно.
        void DisableMpvWindow()
        {
            if (mpvWnd != IntPtr.Zero && Native.IsWindow(mpvWnd) && !Native.IsWindowEnabled(mpvWnd)) return;
            Native.EnumChildWindows(video.Handle, enumCb, IntPtr.Zero);
        }

        bool OnEnumChild(IntPtr h, IntPtr l)
        {
            if (Native.IsWindowEnabled(h)) Native.EnableWindow(h, false);
            mpvWnd = h;
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && !fullscreen && !maximized) // WM_NCHITTEST: изменение размера за края
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1)
                {
                    long lp = m.LParam.ToInt64();
                    var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    int g = Grip;
                    bool l = pt.X < g, r = pt.X >= ClientSize.Width - g, t = pt.Y < g, b = pt.Y >= ClientSize.Height - g;
                    int res = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 1;
                    m.Result = (IntPtr)res;
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys k)
        {
            if (HandleKey(k)) return true;
            return base.ProcessCmdKey(ref msg, k);
        }

        bool HandleKey(Keys k)
        {
            switch (k)
            {
                case Keys.Control | Keys.O: OpenDialog(); return true;
                case Keys.Escape: if (fullscreen) { SetFullscreen(false); return true; } return false;
                case Keys.F: case Keys.Enter: case Keys.F11: SetFullscreen(!fullscreen); return true;
                case Keys.L: TogglePlaylist(); return true;
            }
            if (mpv == null || currentPath == null) return false;
            bool img = ImageMode;
            switch (k)
            {
                case Keys.Space: case Keys.K: TogglePlay(); return true;
                case Keys.Left: if (img) Step(-1); else Seek(-5); return true;
                case Keys.Right: if (img) Step(1); else Seek(5); return true;
                case Keys.Shift | Keys.Left: Seek(-1); return true;
                case Keys.Shift | Keys.Right: Seek(1); return true;
                case Keys.Control | Keys.Left: Seek(-30); return true;
                case Keys.Control | Keys.Right: Seek(30); return true;
                case Keys.Up: AddVolume(5); return true;
                case Keys.Down: AddVolume(-5); return true;
                case Keys.M: ToggleMute(); return true;
                case Keys.PageUp: case Keys.P: Step(-1); return true;
                case Keys.PageDown: case Keys.N: Step(1); return true;
                case Keys.Home: mpv.Cmd("seek", "0", "absolute"); Kick(); return true;
                case Keys.Oemplus: case Keys.Add: ZoomAt(new Point(video.ClientSize.Width / 2, video.ClientSize.Height / 2), 0.25); return true;
                case Keys.OemMinus: case Keys.Subtract: ZoomAt(new Point(video.ClientSize.Width / 2, video.ClientSize.Height / 2), -0.25); return true;
                case Keys.D0: case Keys.NumPad0: ResetZoom(); return true;
                case Keys.R: Rotate(); return true;
                case Keys.S: mpv.Cmd("screenshot"); return true;
                case Keys.OemOpenBrackets: AddSpeed(-0.25); return true;
                case Keys.OemCloseBrackets: AddSpeed(0.25); return true;
                case Keys.Back: AddSpeed(0); return true;
                case Keys.OemPeriod: mpv.Cmd("frame-step"); return true;
                case Keys.Oemcomma: mpv.Cmd("frame-back-step"); return true;
            }
            return false;
        }

        // ── Меню ──
        ToolStripMenuItem Item(ToolStripItemCollection to, string text, Action a, string keys = null, bool check = false)
        {
            var it = new ToolStripMenuItem(text) { Checked = check };
            if (keys != null) it.ShortcutKeyDisplayString = keys;
            if (a != null) it.Click += (s, e) => a();
            to.Add(it);
            return it;
        }

        List<TrackInfo> GetTracks()
        {
            var list = new List<TrackInfo>();
            int n;
            if (mpv == null || !int.TryParse(mpv.Get("track-list/count"), out n)) return list;
            for (int i = 0; i < n; i++)
            {
                string p = "track-list/" + i + "/";
                var t = new TrackInfo { Type = mpv.Get(p + "type"), Id = mpv.Get(p + "id"), Selected = mpv.Get(p + "selected") == "yes" };
                var parts = new List<string>();
                string tt = mpv.Get(p + "title"), lang = mpv.Get(p + "lang"), codec = mpv.Get(p + "codec");
                if (!string.IsNullOrEmpty(tt)) parts.Add(tt);
                if (!string.IsNullOrEmpty(lang)) parts.Add("[" + lang + "]");
                if (!string.IsNullOrEmpty(codec)) parts.Add(codec);
                if (mpv.Get(p + "external") == "yes") parts.Add(L.T("(внешний файл)", "(external file)"));
                t.Label = "#" + t.Id + "   " + string.Join("  ", parts);
                list.Add(t);
            }
            return list;
        }

        void ShowMenu(Point screen, ToolStripDropDownDirection dir)
        {
            var m = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.UI };
            Item(m.Items, L.T("Открыть файл…", "Open file…"), OpenDialog, "Ctrl+O");
            if (currentPath != null)
            {
                string cp = currentPath;
                Item(m.Items, L.T("Показать в проводнике", "Show in Explorer"), () => { try { Process.Start("explorer.exe", "/select,\"" + cp + "\""); } catch { } });
                m.Items.Add(new ToolStripSeparator());
                if (!ImageMode)
                {
                    var tracks = GetTracks();
                    var audio = tracks.Where(t => t.Type == "audio").ToList();
                    if (audio.Count > 1 && kind != MediaKind.Audio)
                    {
                        var am = Item(m.Items, L.T("Аудиодорожка", "Audio track"), null);
                        foreach (var t in audio) { string id = t.Id; Item(am.DropDownItems, t.Label, () => mpv.Set("aid", id), null, t.Selected); }
                    }
                    if (kind != MediaKind.Audio)
                    {
                        var subs = tracks.Where(t => t.Type == "sub").ToList();
                        var sm = Item(m.Items, L.T("Субтитры", "Subtitles"), null);
                        Item(sm.DropDownItems, L.T("Выключить", "Off"), () => mpv.Set("sid", "no"), null, !subs.Any(t => t.Selected));
                        foreach (var t in subs) { string id = t.Id; Item(sm.DropDownItems, t.Label, () => mpv.Set("sid", id), null, t.Selected); }
                        sm.DropDownItems.Add(new ToolStripSeparator());
                        Item(sm.DropDownItems, L.T("Загрузить из файла…", "Load from file…"), LoadSubtitles);
                    }
                    var sp = Item(m.Items, L.T("Скорость", "Speed"), null);
                    foreach (var v in new[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3 })
                    {
                        double vv = v;
                        Item(sp.DropDownItems, v.ToString("0.##", Inv) + "×", () => mpv.Set("speed", vv), null, Math.Abs(speed - v) < 0.001);
                    }
                    Item(m.Items, L.T("Повтор", "Repeat"), ToggleLoop, null, loop);
                }
                else Item(m.Items, files.Count > 1 ? (slideTimer.Enabled ? L.T("Остановить слайд-шоу", "Stop slideshow") : L.T("Слайд-шоу", "Slideshow")) : L.T("Слайд-шоу", "Slideshow"), TogglePlay, L.T("Пробел", "Space")).Enabled = files.Count > 1;
                if (kind != MediaKind.Audio)
                {
                    if (!ImageMode)
                    {
                        var ar = Item(m.Items, L.T("Соотношение сторон", "Aspect ratio"), null);
                        string curAr = mpv.Get("video-aspect-override") ?? "-1";
                        foreach (var a in new[] { new[] { L.T("Авто", "Auto"), "-1" }, new[] { "16:9", "16:9" }, new[] { "4:3", "4:3" }, new[] { "21:9", "2.333" }, new[] { "1:1", "1:1" } })
                        {
                            string val = a[1];
                            bool sel = val == "-1" ? (curAr == "-1" || curAr == "no") : CloseRatio(curAr, val);
                            Item(ar.DropDownItems, a[0], () => mpv.Set("video-aspect-override", val), null, sel);
                        }
                    }
                    Item(m.Items, L.T("Повернуть на 90°", "Rotate 90°"), Rotate, "R");
                    if (ImageMode) Item(m.Items, L.T("По размеру окна", "Fit to window"), ResetZoom, "0");
                    Item(m.Items, L.T("Скриншот на рабочий стол", "Screenshot to desktop"), () => mpv.Cmd("screenshot"), "S");
                }
            }
            m.Items.Add(new ToolStripSeparator());
            Item(m.Items, L.T("Плейлист", "Playlist"), TogglePlaylist, "L", plist.Visible);
            Item(m.Items, L.T("Поверх всех окон", "Always on top"), () => TopMost = !TopMost, null, TopMost);
            Item(m.Items, L.T("Полноэкранный режим", "Fullscreen"), () => SetFullscreen(!fullscreen), "F", fullscreen);
            m.Items.Add(new ToolStripSeparator());
            var lm = Item(m.Items, "Language / Язык", null);
            Item(lm.DropDownItems, L.T("Как в Windows", "Same as Windows"), () => SetLanguage(L.Auto), null, L.Mode == L.Auto);
            lm.DropDownItems.Add(new ToolStripSeparator());
            foreach (int lang in L.Menu)
            {
                int code = lang;
                Item(lm.DropDownItems, L.Name(code), () => SetLanguage(code), null, L.Mode == code);
            }
            Item(m.Items, L.T("Сделать плеером по умолчанию…", "Set as default player…"), MakeDefault);
            Item(m.Items, L.T("Удалить AlenPlayer из системы", "Remove AlenPlayer from system"), RemoveDefault);
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(screen, dir);
        }

        // Язык меняется сразу: меню собирается заново при каждом открытии, остальное перерисовываем
        void SetLanguage(int mode)
        {
            L.Init(mode);
            settings.Lang = mode;
            settings.Save();
            welcome.Invalidate();
            plist.Invalidate();
            title.Invalidate();
        }

        void MakeDefault()
        {
            try { Assoc.Register(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("Не удалось зарегистрировать форматы: ", "Failed to register file types: ") + ex.Message, "AlenPlayer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            MessageBox.Show(this,
                L.T("AlenPlayer зарегистрирован для всех видео, аудио и фото.\n\n" +
                    "Сейчас откроются Параметры Windows → Приложения по умолчанию → AlenPlayer.\n" +
                    "Нажмите там кнопку «Установить по умолчанию» вверху страницы.\n\n" +
                    "Если такой кнопки нет (Windows 10), выберите AlenPlayer для нужных форматов в списке ниже.\n\n" +
                    "Совет: сначала положите AlenPlayer.exe в постоянную папку — если exe перенести, просто запустите его ещё раз.",
                    "AlenPlayer is now registered for all video, audio and image files.\n\n" +
                    "Windows Settings → Default apps → AlenPlayer will open now.\n" +
                    "Click the \"Set default\" button at the top of the page.\n\n" +
                    "If there is no such button (Windows 10), choose AlenPlayer for the file types you need in the list below.\n\n" +
                    "Tip: put AlenPlayer.exe in a permanent folder first. If you move it later, just run it once again."),
                L.T("Плеер по умолчанию", "Default player"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            Assoc.OpenSettings();
        }

        void RemoveDefault()
        {
            if (MessageBox.Show(this, L.T("Убрать AlenPlayer из «Открыть с помощью», ассоциаций файлов и миниатюр Проводника?\n" +
                                      "При следующем запуске плеер снова включит свои миниатюры.",
                                      "Remove AlenPlayer from \"Open with\", file associations and Explorer thumbnails?\n" +
                                      "Thumbnails will be turned on again the next time you start the player."),
                    "AlenPlayer", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try { Assoc.Unregister(); Osd(L.T("AlenPlayer удалён из системы", "AlenPlayer removed from system"), 2000); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "AlenPlayer"); }
        }

        static bool CloseRatio(string a, string b)
        {
            Func<string, double> parse = s =>
            {
                double x, y;
                int i = s.IndexOf(':');
                if (i > 0 && double.TryParse(s.Substring(0, i), NumberStyles.Float, Inv, out x) && double.TryParse(s.Substring(i + 1), NumberStyles.Float, Inv, out y) && y != 0) return x / y;
                return double.TryParse(s, NumberStyles.Float, Inv, out x) ? x : -1;
            };
            return Math.Abs(parse(a) - parse(b)) < 0.01;
        }

        void LoadSubtitles()
        {
            using (var d = new OpenFileDialog { Filter = L.T("Субтитры", "Subtitles") + "|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.sup;*.idx;*.smi|" + L.T("Все файлы", "All files") + "|*.*", Title = L.T("Загрузить субтитры", "Load subtitles") })
            {
                if (currentPath != null) d.InitialDirectory = Path.GetDirectoryName(currentPath);
                if (d.ShowDialog(this) == DialogResult.OK) mpv.Cmd("sub-add", d.FileName, "select");
            }
        }

        // ── Закрытие ──
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            var b = maximized || fullscreen ? normalBounds : Bounds;
            settings.Volume = volume; settings.Loop = loop;
            settings.X = b.X; settings.Y = b.Y; settings.W = b.Width; settings.H = b.Height; settings.Max = maximized;
            settings.Save();
            Native.SetThreadExecutionState(0x80000000);
            uiTimer.Stop(); slideTimer.Stop();
            if (cursorHidden) SetCursorHidden(false);
            osd.HideNow(); leftArrow.HideNow(); rightArrow.HideNow();
            if (mpv == null) return;

            var p = mpv;
            string[] pre = ShouldSaveResume() ? new[] { "write-watch-later-config" }
                         : (currentPath != null && kind != MediaKind.Image && dur > 300 ? new[] { "delete-watch-later-config" } : new string[0]);
            mpv = null;
            Hide();
            // Движок закрываем в фоне, а UI-поток продолжает обрабатывать сообщения —
            // окно mpv является дочерним для нашего, иначе возможна взаимная блокировка.
            var done = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(_ => { try { p.Quit(pre); } catch { } done.Set(); });
            var sw = Stopwatch.StartNew();
            while (!done.WaitOne(10) && sw.ElapsedMilliseconds < 5000) Application.DoEvents();
        }
    }
}
