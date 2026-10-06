using System;

namespace SubScreen.Core
{
    /// <summary>图标来自哪个字体。</summary>
    internal enum IconFontKind
    {
        /// <summary>Segoe Fluent Icons（Windows 11 自带），用于界面控件图标。</summary>
        Fluent = 0,
        /// <summary>Segoe UI Symbol，用于标准 Unicode 天气符号。</summary>
        Symbol = 1
    }

    /// <summary>一个图标：字体 + 码位。</summary>
    internal struct IconSpec
    {
        public readonly IconFontKind Font;
        public readonly char Code;

        public IconSpec(IconFontKind font, int codePoint)
        {
            Font = font;
            Code = (char)codePoint;
        }

        public bool IsEmpty
        {
            get { return Code == '\0'; }
        }
    }

    /// <summary>
    /// 全部图标的码位。这些码位都是在 Windows 11 (build 26300) 上
    /// 用 GDI 实际渲染确认过的，不是从文档抄的。
    /// </summary>
    internal static class Icons
    {
        private static IconSpec F(int cp) { return new IconSpec(IconFontKind.Fluent, cp); }
        private static IconSpec S(int cp) { return new IconSpec(IconFontKind.Symbol, cp); }

        // ---- Segoe Fluent Icons（已渲染确认）----
        public static readonly IconSpec Gear = F(0xE713);
        public static readonly IconSpec GearAlt = F(0xE9F5);
        public static readonly IconSpec Clock = F(0xE823);
        public static readonly IconSpec Calendar = F(0xE787);
        public static readonly IconSpec Play = F(0xE768);
        public static readonly IconSpec Pause = F(0xE769);
        public static readonly IconSpec Stop = F(0xE76A);
        public static readonly IconSpec Prev = F(0xE892);
        public static readonly IconSpec Next = F(0xE893);
        public static readonly IconSpec Close = F(0xE894);
        /// <summary>最小化（一条横杠）。</summary>
        public static readonly IconSpec Minimize = F(0xE921);
        public static readonly IconSpec Refresh = F(0xE895);
        public static readonly IconSpec ChevronLeft = F(0xE76B);
        public static readonly IconSpec ChevronRight = F(0xE76C);
        public static readonly IconSpec ChevronUp = F(0xE70E);
        public static readonly IconSpec ChevronDown = F(0xE70D);
        public static readonly IconSpec Music = F(0xE8D6);
        public static readonly IconSpec Speaker = F(0xE767);
        public static readonly IconSpec SpeakerMute = F(0xE74F);
        public static readonly IconSpec Thermometer = F(0xE9CA);
        /// <summary>水滴，用于湿度。</summary>
        public static readonly IconSpec Humidity = F(0xEB42);
        /// <summary>圆形表盘，用于气压。</summary>
        public static readonly IconSpec Gauge = F(0xF42F);
        /// <summary>半圆速度表，用于风速（该字体里没有"风"字形，风速本身就是速度指标）。</summary>
        public static readonly IconSpec Speedometer = F(0xEC48);
        public static readonly IconSpec Power = F(0xE7E8);
        public static readonly IconSpec Home = F(0xE80F);
        public static readonly IconSpec Geo = F(0xE81D);
        public static readonly IconSpec Pin = F(0xE718);
        public static readonly IconSpec FluentSun = F(0xE706);
        public static readonly IconSpec FluentMoon = F(0xE708);
        public static readonly IconSpec FluentCloud = F(0xE753);

        // ---- Segoe UI Symbol（标准 Unicode 天气符号，已渲染确认）----
        public static readonly IconSpec Sun = S(0x2600);
        public static readonly IconSpec SunRays = S(0x263C);
        public static readonly IconSpec Cloud = S(0x2601);
        public static readonly IconSpec PartlyCloudy = S(0x26C5);
        public static readonly IconSpec Rain = S(0x2614);
        public static readonly IconSpec RainAlt = S(0x26C6);
        public static readonly IconSpec Thunder = S(0x26C8);
        public static readonly IconSpec Bolt = S(0x26A1);
        public static readonly IconSpec Snow = S(0x2744);
        public static readonly IconSpec SnowAlt = S(0x2745);
        public static readonly IconSpec Snowman = S(0x2603);
        public static readonly IconSpec Moon = S(0x263E);
        public static readonly IconSpec MoonFirstQuarter = S(0x263D);
        public static readonly IconSpec Warn = S(0x26A0);
    }

    /// <summary>和风天气 icon 代码 -> 图标。</summary>
    internal static class WeatherIcon
    {
        /// <summary>
        /// 和风天气 v7 图标代码表（100-999）。
        /// 说明：雾 / 霾 / 浮尘 / 扬沙 在 Segoe UI Symbol 里没有对应的标准符号，
        /// 统一用云朵表示，具体天气现象由旁边的文字（如"雾""中度霾"）说明；
        /// 沙尘暴级别用警告符号强调。
        /// </summary>
        public static IconSpec ForCode(int code, bool night)
        {
            // 晴
            if (code == 100)
            {
                return night ? Icons.Moon : Icons.Sun;
            }

            // 多云 / 少云 / 晴间多云 / 阴
            if (code == 101 || code == 102 || code == 103 || code == 104
                || code == 151 || code == 152 || code == 153 || code == 154)
            {
                if (night)
                {
                    return Icons.Cloud;
                }
                return (code == 103 || code == 102) ? Icons.PartlyCloudy : Icons.Cloud;
            }

            // 风类：轻风用云表示，大风以上用警告
            if (code >= 200 && code <= 213)
            {
                return code >= 207 ? Icons.Warn : Icons.Cloud;
            }

            // 雷阵雨类
            if (code == 302 || code == 303 || code == 304)
            {
                return Icons.Thunder;
            }

            // 雨类
            if (code >= 300 && code <= 399)
            {
                return Icons.Rain;
            }

            // 雨夹雪 / 雨雪
            if (code == 404 || code == 405 || code == 406)
            {
                return Icons.SnowAlt;
            }

            // 雪类
            if (code >= 400 && code <= 499)
            {
                return Icons.Snow;
            }

            // 沙尘暴 / 强沙尘暴
            if (code == 507 || code == 508)
            {
                return Icons.Warn;
            }

            // 雾 / 霾 / 浮尘 / 扬沙
            if (code >= 500 && code <= 515)
            {
                return Icons.Cloud;
            }

            if (code == 900)
            {
                return Icons.SunRays;
            }
            if (code == 901)
            {
                return Icons.Snow;
            }

            return night ? Icons.Moon : Icons.Sun;
        }
    }
}
