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
    // ───────────────────────────── Форматы ─────────────────────────────
    enum MediaKind { None, Video, Audio, Image }

    static class Formats
    {
        public static readonly HashSet<string> Video = Set("mp4 m4v mkv webm avi mov qt wmv asf flv f4v mpg mpeg mpe m2v vob ts m2ts mts tp trp 3gp 3g2 ogv ogm rm rmvb divx xvid mxf dv nut y4m hevc h264 h265 264 265 av1 ivf amv wtv dvr-ms mjpeg");
        public static readonly HashSet<string> Audio = Set("mp3 flac wav wave ogg oga opus m4a m4b aac wma ape alac aif aiff aifc wv mka ac3 eac3 dts thd mlp amr awb tta mpc mp2 mp1 caf au snd voc w64 spx dsf dff gsm ra xm mod s3m it");
        public static readonly HashSet<string> Image = Set("jpg jpeg jpe jfif png apng gif bmp dib webp tif tiff ico cur avif heic heif jxl tga dds exr hdr pbm pgm ppm pnm pam pcx qoi psd sgi xbm xwd j2k jp2 svg");
        // Форматы, для которых у Windows есть хорошие собственные миниатюры
        public static readonly HashSet<string> NativeThumbImages = Set("jpg jpeg jpe jfif png gif bmp dib webp tif tiff ico cur avif heic heif jxl");

        static HashSet<string> Set(string s)
        {
            return new HashSet<string>(s.Split(' ').Select(x => "." + x), StringComparer.OrdinalIgnoreCase);
        }

        public static MediaKind Of(string path)
        {
            string e = Path.GetExtension(path) ?? "";
            if (Video.Contains(e)) return MediaKind.Video;
            if (Audio.Contains(e)) return MediaKind.Audio;
            if (Image.Contains(e)) return MediaKind.Image;
            return MediaKind.None;
        }

        static string Mask(HashSet<string> s) { return string.Join(";", s.Select(x => "*" + x)); }

        public static string Filter()
        {
            string all = Mask(Video) + ";" + Mask(Audio) + ";" + Mask(Image);
            return L.T("Все медиафайлы", "All media files") + "|" + all + "|" + L.T("Видео", "Video") + "|" + Mask(Video) + "|" +
                   L.T("Аудио", "Audio") + "|" + Mask(Audio) + "|" + L.T("Изображения", "Images") + "|" + Mask(Image) + "|" +
                   L.T("Все файлы", "All files") + "|*.*";
        }
    }
}
