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
    // ───────────────────────────── Настройки ─────────────────────────────
    class Settings
    {
        public double Volume = 80;
        public int X, Y, W, H;
        public bool Max, Loop;
        public int Lang = L.Auto;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlenPlayer"); }
        }

        // Перенос настроек со старого имени и удаление старого кэша движка (раньше он распаковывался в AppData)
        public static void MigrateOld()
        {
            try
            {
                string oldIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovaPlayer", "settings.ini");
                string ini = Path.Combine(Dir, "settings.ini");
                if (File.Exists(oldIni) && !File.Exists(ini)) { Directory.CreateDirectory(Dir); File.Copy(oldIni, ini); }
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var old = new List<string> { Path.Combine(local, "NovaPlayer") };
                string ap = Path.Combine(local, "AlenPlayer");
                if (Directory.Exists(ap)) old.AddRange(Directory.GetDirectories(ap, "engine-*"));
                foreach (var d in old) if (Directory.Exists(d)) try { Directory.Delete(d, true); } catch { }
            }
            catch { }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                foreach (var line in File.ReadAllLines(Path.Combine(Dir, "settings.ini")))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim();
                    double d;
                    if (!double.TryParse(line.Substring(i + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) continue;
                    switch (k)
                    {
                        case "volume": s.Volume = Math.Max(0, Math.Min(100, d)); break;
                        case "x": s.X = (int)d; break;
                        case "y": s.Y = (int)d; break;
                        case "w": s.W = (int)d; break;
                        case "h": s.H = (int)d; break;
                        case "max": s.Max = d != 0; break;
                        case "loop": s.Loop = d != 0; break;
                        case "lang": s.Lang = (int)d; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, "settings.ini"), string.Format(CultureInfo.InvariantCulture,
                    "volume={0}\nx={1}\ny={2}\nw={3}\nh={4}\nmax={5}\nloop={6}\nlang={7}\n", Volume, X, Y, W, H, Max ? 1 : 0, Loop ? 1 : 0, Lang));
            }
            catch { }
        }
    }

    class TrackInfo { public string Type, Id, Label; public bool Selected; }
}
