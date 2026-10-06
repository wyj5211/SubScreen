using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;

namespace SubScreen.Core
{
    /// <summary>
    /// 字体解析与缓存。
    /// 图标字体：
    ///   1. 如果 config/fonts 下放了 Font Awesome 5 Free-Solid-900.otf，会优先加载它（预留的 FA5 接入点）；
    ///   2. 否则用系统自带的 Segoe Fluent Icons（界面图标）+ Segoe UI Symbol（天气符号）。
    /// </summary>
    internal static class IconFont
    {
        private static readonly Dictionary<string, Font> Cache =
            new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new object();

        private static PrivateFontCollection _privateFonts;
        private static string _fluentFamily;
        private static string _symbolFamily;
        private static string _textFamily;
        private static string _displayFamily;

        public static string FluentFamily
        {
            get { EnsureResolved(); return _fluentFamily; }
        }

        public static string SymbolFamily
        {
            get { EnsureResolved(); return _symbolFamily; }
        }

        /// <summary>中文界面正文字体。</summary>
        public static string TextFamily
        {
            get { EnsureResolved(); return _textFamily; }
        }

        /// <summary>大号数字/时钟用字体。</summary>
        public static string DisplayFamily
        {
            get { EnsureResolved(); return _displayFamily; }
        }

        public static bool UsingFontAwesome { get; private set; }

        private static void EnsureResolved()
        {
            if (_fluentFamily != null)
            {
                return;
            }

            lock (Sync)
            {
                if (_fluentFamily != null)
                {
                    return;
                }

                string fa = TryLoadFontAwesome();
                if (fa != null)
                {
                    UsingFontAwesome = true;
                    _fluentFamily = fa;
                }
                else
                {
                    _fluentFamily = PickFamily("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol");
                }

                _symbolFamily = PickFamily("Segoe UI Symbol", "Segoe UI", "Segoe Fluent Icons");
                _textFamily = PickFamily("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma");
                _displayFamily = PickFamily(
                    "Segoe UI Variable Display", "Segoe UI", "Microsoft YaHei UI", "Tahoma");
            }
        }

        /// <summary>
        /// 如果用户把 Font Awesome 5 字体丢进 config/fonts，就加载它。
        /// 返回字体族名；没有则返回 null。
        /// </summary>
        private static string TryLoadFontAwesome()
        {
            try
            {
                if (!Directory.Exists(AppPaths.FontsDir))
                {
                    return null;
                }

                string[] candidates = Directory.GetFiles(AppPaths.FontsDir, "*.otf");
                string[] ttf = Directory.GetFiles(AppPaths.FontsDir, "*.ttf");

                var all = new List<string>();
                all.AddRange(candidates);
                all.AddRange(ttf);
                if (all.Count == 0)
                {
                    return null;
                }

                _privateFonts = new PrivateFontCollection();
                foreach (string file in all)
                {
                    try
                    {
                        _privateFonts.AddFontFile(file);
                    }
                    catch (Exception)
                    {
                        // 单个字体文件坏了不影响其它
                    }
                }

                if (_privateFonts.Families.Length == 0)
                {
                    return null;
                }

                foreach (FontFamily fam in _privateFonts.Families)
                {
                    string n = fam.Name;
                    if (n.IndexOf("Awesome", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return n;
                    }
                }

                return _privateFonts.Families[0].Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string PickFamily(params string[] names)
        {
            foreach (string want in names)
            {
                try
                {
                    using (var f = new Font(want, 12f))
                    {
                        // Font 构造会自动回落到别的字体，用 Name 判断是否命中
                        if (string.Equals(f.Name, want, StringComparison.OrdinalIgnoreCase))
                        {
                            return want;
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            // 一个都没命中，用系统默认
            return FontFamily.GenericSansSerif.Name;
        }

        /// <summary>取图标字体，按像素大小缓存。</summary>
        public static Font GetIcon(IconFontKind kind, float pixelSize)
        {
            string family = kind == IconFontKind.Fluent ? FluentFamily : SymbolFamily;
            return GetCached(family, pixelSize, FontStyle.Regular);
        }

        /// <summary>取正文/数字字体。</summary>
        public static Font GetText(float pixelSize, FontStyle style)
        {
            return GetCached(TextFamily, pixelSize, style);
        }

        /// <summary>取大号显示字体（时钟、温度这类数字）。</summary>
        public static Font GetDisplay(float pixelSize, FontStyle style)
        {
            return GetCached(DisplayFamily, pixelSize, style);
        }

        /// <summary>兼容旧系统上 Segoe UI Variable 的等宽数字需求：直接用正文字体。</summary>
        public static Font GetNumeric(float pixelSize, FontStyle style)
        {
            return GetCached(TextFamily, pixelSize, style);
        }

        private static Font GetCached(string family, float pixelSize, FontStyle style)
        {
            if (pixelSize < 1f)
            {
                pixelSize = 1f;
            }

            // 量化到 0.5px，避免缓存无限膨胀
            float size = (float)(Math.Round(pixelSize * 2) / 2.0);
            string key = family + "|" + size.ToString("0.0") + "|" + (int)style;

            lock (Sync)
            {
                Font f;
                if (Cache.TryGetValue(key, out f))
                {
                    return f;
                }

                try
                {
                    f = new Font(family, size, style, GraphicsUnit.Pixel);
                }
                catch (Exception)
                {
                    f = new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel);
                }

                Cache[key] = f;
                return f;
            }
        }

        public static void ClearCache()
        {
            lock (Sync)
            {
                foreach (Font f in Cache.Values)
                {
                    try
                    {
                        f.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
                Cache.Clear();
            }
        }
    }
}
