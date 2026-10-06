using System;
using System.Drawing;
using Microsoft.Win32;

namespace SubScreen.Core
{
    /// <summary>读取 Windows 强调色，并按主题做对比度修正。</summary>
    internal static class AccentColor
    {
        private const string ExplorerAccentKey =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        private const string DwmKey = @"Software\Microsoft\Windows\DWM";

        private static Color? _cached;

        /// <summary>读取系统强调色（不做对比度修正）。</summary>
        public static Color GetSystemAccent()
        {
            if (_cached.HasValue)
            {
                return _cached.Value;
            }

            Color result = ReadAccentCore();
            _cached = result;
            return result;
        }

        /// <summary>系统强调色可能被用户改了，设置页打开时清一次缓存。</summary>
        public static void InvalidateCache()
        {
            _cached = null;
        }

        private static Color ReadAccentCore()
        {
            // 1) Explorer\Accent\AccentColorMenu —— 存储格式 0xAABBGGRR
            Color c;
            if (TryReadDword(Registry.CurrentUser, ExplorerAccentKey, "AccentColorMenu", out c, true))
            {
                return c;
            }

            // 2) Explorer\Accent\StartColorMenu —— 同样是 0xAABBGGRR
            if (TryReadDword(Registry.CurrentUser, ExplorerAccentKey, "StartColorMenu", out c, true))
            {
                return c;
            }

            // 3) DWM\AccentColor —— 0xAABBGGRR
            if (TryReadDword(Registry.CurrentUser, DwmKey, "AccentColor", out c, true))
            {
                return c;
            }

            // 4) DWM\ColorizationColor —— 0x00RRGGBB
            if (TryReadDword(Registry.CurrentUser, DwmKey, "ColorizationColor", out c, false))
            {
                return c;
            }

            // 5) 最后回落到系统高亮色
            return SystemColors.Highlight;
        }

        private static bool TryReadDword(RegistryKey hive, string subKey, string name, out Color color, bool abgr)
        {
            color = Color.Empty;
            try
            {
                using (RegistryKey key = hive.OpenSubKey(subKey))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    object raw = key.GetValue(name);
                    if (raw == null)
                    {
                        return false;
                    }

                    int v = Convert.ToInt32(raw);
                    int b;
                    int g;
                    int r;
                    if (abgr)
                    {
                        // 0xAABBGGRR
                        b = (v >> 16) & 0xFF;
                        g = (v >> 8) & 0xFF;
                        r = v & 0xFF;
                    }
                    else
                    {
                        // 0x00RRGGBB
                        r = (v >> 16) & 0xFF;
                        g = (v >> 8) & 0xFF;
                        b = v & 0xFF;
                    }

                    // 全黑说明这个键没被写有效值，视为失败继续兜底
                    if (r == 0 && g == 0 && b == 0)
                    {
                        return false;
                    }

                    color = Color.FromArgb(255, r, g, b);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>按当前设置解析出最终要用的强调色。</summary>
        public static Color Resolve(AppSettings settings)
        {
            if (settings.AccentMode == AccentMode.Custom)
            {
                return settings.CustomAccent;
            }

            return AdjustForTheme(GetSystemAccent(), settings.Theme);
        }

        /// <summary>把强调色调整到在对应主题下足够可读。</summary>
        public static Color AdjustForTheme(Color accent, AppTheme theme)
        {
            double lum = ColorUtil.Luminance(accent);

            if (theme == AppTheme.Dark)
            {
                // 深色主题下用偏亮的强调色（Windows 也是这么做的）
                if (lum < 0.55)
                {
                    double t = (0.55 - lum) / (1.0 - lum);
                    return ColorUtil.Shade(accent, Math.Min(0.55, t));
                }
            }
            else
            {
                if (lum > 0.55)
                {
                    double t = (lum - 0.55) / lum;
                    return ColorUtil.Shade(accent, -Math.Min(0.5, t));
                }
            }

            return accent;
        }
    }
}
