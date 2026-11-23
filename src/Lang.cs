using System.Globalization;

namespace Alen
{
    // ───────────────────────────── Язык интерфейса ─────────────────────────────
    // Строки в коде: L.T("русский", "english"). Остальные языки — в LangData.cs
    // (генерируется из tools/translations.py, ключ — английская строка).
    static partial class L
    {
        public const int Auto = 0, Russian = 1, English = 2, Ukrainian = 3, Kazakh = 4, Uzbek = 5,
                         Spanish = 6, German = 7, French = 8, Portuguese = 9, Turkish = 10, Chinese = 11;

        // Порядок в меню и самоназвания языков
        public static readonly int[] Menu = { English, Russian, Ukrainian, Kazakh, Uzbek, Spanish, German, French, Portuguese, Turkish, Chinese };
        static readonly string[] names = { "", "Русский", "English", "Українська", "Қазақша", "O'zbekcha",
                                           "Español", "Deutsch", "Français", "Português", "Türkçe", "中文（简体）" };
        public static string Name(int lang) { return names[lang]; }

        public static int Mode { get; private set; }   // что выбрано в меню (0 = как в Windows)
        public static int Cur { get; private set; }    // какой язык действует сейчас

        public static void Init(int mode)
        {
            Mode = mode >= Russian && mode <= Chinese ? mode : Auto;
            Cur = Mode == Auto ? Detect() : Mode;
        }

        static int Detect()
        {
            switch (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            {
                case "ru": case "be": case "ky": case "tg": return Russian;
                case "uk": return Ukrainian;
                case "kk": return Kazakh;
                case "uz": return Uzbek;
                case "es": return Spanish;
                case "de": return German;
                case "fr": return French;
                case "pt": return Portuguese;
                case "tr": return Turkish;
                case "zh": return Chinese;
                default: return English;
            }
        }

        public static string T(string ru, string en)
        {
            if (Cur == Russian) return ru;
            if (Cur == English) return en;
            string[] t;
            return Tr.TryGetValue(en, out t) ? t[Cur - Ukrainian] : en;
        }

        public static string Files(int n)
        {
            int a = n % 100, b = n % 10;
            bool few = b >= 2 && b <= 4 && (a < 12 || a > 14), one = b == 1 && a != 11;
            switch (Cur)
            {
                case Russian: return n + (one ? " файл" : few ? " файла" : " файлов");
                case Ukrainian: return n + (one ? " файл" : few ? " файли" : " файлів");
                case Kazakh: return n + " файл";
                case Uzbek: return n + " ta fayl";
                case Spanish: return n + (n == 1 ? " archivo" : " archivos");
                case German: return n + (n == 1 ? " Datei" : " Dateien");
                case French: return n + (n <= 1 ? " fichier" : " fichiers");
                case Portuguese: return n + (n == 1 ? " arquivo" : " arquivos");
                case Turkish: return n + " dosya";
                case Chinese: return n + " 个文件";
                default: return n + (n == 1 ? " file" : " files");
            }
        }
    }
}
