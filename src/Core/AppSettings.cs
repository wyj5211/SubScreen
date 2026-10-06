using System;
using System.Drawing;

namespace SubScreen.Core
{
    internal enum BackgroundKind
    {
        SolidColor = 0,
        Image = 1
    }

    /// <summary>图片适应模式。</summary>
    internal enum ImageFitMode
    {
        /// <summary>填充：等比放大铺满，超出部分裁掉。</summary>
        Fill = 0,
        /// <summary>适应：等比缩放到完整可见，留黑边。</summary>
        Fit = 1,
        /// <summary>拉伸：不保持比例拉满。</summary>
        Stretch = 2,
        /// <summary>平铺：原始尺寸重复平铺。</summary>
        Tile = 3,
        /// <summary>居中：原始尺寸居中，不缩放。</summary>
        Center = 4,
        /// <summary>缩放：只放大不缩小，保持比例。</summary>
        Zoom = 5
    }

    internal enum AppTheme
    {
        Dark = 0,
        Light = 1
    }

    internal enum AccentMode
    {
        /// <summary>跟随 Windows 强调色。</summary>
        System = 0,
        /// <summary>自定义强调色。</summary>
        Custom = 1
    }

    internal enum ScreenChoice
    {
        /// <summary>自动挑一块副屏（非主屏中面积最大的）。</summary>
        Auto = 0,
        Primary = 1,
        /// <summary>按 ScreenDevice 指定的设备名。</summary>
        Specific = 2
    }

    /// <summary>
    /// 全部设置项。用单例 Current 访问，改完调用 Save()。
    /// 所有字段都有合理默认值，缺失的 INI 键会回落到默认值。
    /// </summary>
    internal sealed class AppSettings
    {
        private static AppSettings _current;

        public static AppSettings Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Load();
                }
                return _current;
            }
        }

        /// <summary>丢弃缓存，下次访问 Current 时重新读盘。</summary>
        public static void Invalidate()
        {
            _current = null;
        }

        // ---------------- 背景 ----------------
        public BackgroundKind BackgroundKind = BackgroundKind.SolidColor;
        public Color BackgroundColor = Color.FromArgb(0x14, 0x16, 0x1A);
        /// <summary>config/background 下的文件名（如 bg.png），没有背景图时为空。</summary>
        public string BackgroundFileName = string.Empty;
        /// <summary>-100..100</summary>
        public int BackgroundBrightness = 0;
        /// <summary>0..100，映射到模糊半径</summary>
        public int BackgroundBlur = 0;
        public ImageFitMode BackgroundFit = ImageFitMode.Fill;
        /// <summary>0..100，在背景上叠一层纯色遮罩以提升卡片可读性</summary>
        public int BackgroundDim = 0;

        // ---------------- 外观 ----------------
        public AppTheme Theme = AppTheme.Dark;
        /// <summary>卡片背景不透明度 0..100</summary>
        public int CardOpacity = 70;
        public AccentMode AccentMode = AccentMode.System;
        public Color CustomAccent = Color.FromArgb(0x00, 0x78, 0xD4);
        /// <summary>卡片圆角半径（px，按 96 DPI 计）</summary>
        public int CornerRadius = 10;
        /// <summary>左列宽度占比 20..80</summary>
        public int ColumnRatio = 50;
        /// <summary>卡片间距（px）</summary>
        public int TileGap = 14;
        /// <summary>卡片内边距（px）</summary>
        public int TilePadding = 22;
        /// <summary>整个仪表盘距离屏幕边缘的留白（px）</summary>
        public int ScreenMargin = 22;

        // ---------------- 天气 ----------------
        public string WeatherHost = string.Empty;
        public string WeatherKey = string.Empty;
        public string WeatherCity = "北京";
        /// <summary>GeoAPI 解析缓存，避免每次启动都查询</summary>
        public string WeatherLocationId = string.Empty;
        public int WeatherRefreshMinutes = 30;
        public bool WeatherUseCelsius = true;

        // ---------------- 一言 ----------------
        public int HitokotoRefreshMinutes = 60;
        /// <summary>一言分类，用 &amp; 分隔，如 d&amp;i&amp;k；留空表示不限定分类</summary>
        public string HitokotoCategories = string.Empty;

        // ---------------- 音乐播控 ----------------
        public int MediaRefreshSeconds = 1;

        // ---------------- 日历 ----------------
        public bool WeekStartsMonday = true;
        public bool ShowLunar = true;
        public bool ShowSolarTerm = true;
        public bool ShowHoliday = true;

        // ---------------- 常规 ----------------
        public ScreenChoice ScreenChoice = ScreenChoice.Auto;
        public string ScreenDevice = string.Empty;
        /// <summary>是否覆盖副屏任务栏（true = 铺满整屏）</summary>
        public bool CoverTaskbar = true;
        public bool AutoStart = false;
        public bool ClockShowSeconds = false;
        public bool ClockUse24Hour = true;
        /// <summary>天气卡片显示的城市名覆盖（为空则用 GeoAPI 返回值）</summary>
        public string WeatherCityDisplay = string.Empty;

        // ================================================================

        public static AppSettings Load()
        {
            var s = new AppSettings();
            IniFile ini = IniFile.Load(AppPaths.SettingsFile);

            // 背景
            s.BackgroundKind = ini.GetEnum("Background", "Kind", s.BackgroundKind);
            s.BackgroundColor = ini.GetColor("Background", "Color", s.BackgroundColor);
            s.BackgroundFileName = ini.Get("Background", "File", s.BackgroundFileName) ?? string.Empty;
            s.BackgroundBrightness = ini.GetInt("Background", "Brightness", s.BackgroundBrightness, -100, 100);
            s.BackgroundBlur = ini.GetInt("Background", "Blur", s.BackgroundBlur, 0, 100);
            s.BackgroundFit = ini.GetEnum("Background", "Fit", s.BackgroundFit);
            s.BackgroundDim = ini.GetInt("Background", "Dim", s.BackgroundDim, 0, 100);

            // 外观
            s.Theme = ini.GetEnum("Appearance", "Theme", s.Theme);
            s.CardOpacity = ini.GetInt("Appearance", "CardOpacity", s.CardOpacity, 0, 100);
            s.AccentMode = ini.GetEnum("Appearance", "AccentMode", s.AccentMode);
            s.CustomAccent = ini.GetColor("Appearance", "AccentColor", s.CustomAccent);
            s.CornerRadius = ini.GetInt("Appearance", "CornerRadius", s.CornerRadius, 0, 40);
            s.ColumnRatio = ini.GetInt("Appearance", "ColumnRatio", s.ColumnRatio, 20, 80);
            s.TileGap = ini.GetInt("Appearance", "TileGap", s.TileGap, 0, 60);
            s.TilePadding = ini.GetInt("Appearance", "TilePadding", s.TilePadding, 0, 80);
            s.ScreenMargin = ini.GetInt("Appearance", "ScreenMargin", s.ScreenMargin, 0, 200);

            // 天气
            s.WeatherHost = ini.Get("Weather", "Host", s.WeatherHost) ?? string.Empty;
            s.WeatherKey = ini.Get("Weather", "Key", s.WeatherKey) ?? string.Empty;
            s.WeatherCity = ini.Get("Weather", "City", s.WeatherCity) ?? string.Empty;
            s.WeatherLocationId = ini.Get("Weather", "LocationId", s.WeatherLocationId) ?? string.Empty;
            s.WeatherCityDisplay = ini.Get("Weather", "CityDisplay", s.WeatherCityDisplay) ?? string.Empty;
            s.WeatherRefreshMinutes = ini.GetInt("Weather", "RefreshMinutes", s.WeatherRefreshMinutes, 1, 1440);
            s.WeatherUseCelsius = ini.GetBool("Weather", "UseCelsius", s.WeatherUseCelsius);

            // 一言
            s.HitokotoRefreshMinutes = ini.GetInt("Hitokoto", "RefreshMinutes", s.HitokotoRefreshMinutes, 1, 1440);
            s.HitokotoCategories = ini.Get("Hitokoto", "Categories", s.HitokotoCategories) ?? string.Empty;

            // 播控
            s.MediaRefreshSeconds = ini.GetInt("Media", "RefreshSeconds", s.MediaRefreshSeconds, 1, 60);

            // 日历
            s.WeekStartsMonday = ini.GetBool("Calendar", "WeekStartsMonday", s.WeekStartsMonday);
            s.ShowLunar = ini.GetBool("Calendar", "ShowLunar", s.ShowLunar);
            s.ShowSolarTerm = ini.GetBool("Calendar", "ShowSolarTerm", s.ShowSolarTerm);
            s.ShowHoliday = ini.GetBool("Calendar", "ShowHoliday", s.ShowHoliday);

            // 常规
            s.ScreenChoice = ini.GetEnum("General", "ScreenChoice", s.ScreenChoice);
            s.ScreenDevice = ini.Get("General", "ScreenDevice", s.ScreenDevice) ?? string.Empty;
            s.CoverTaskbar = ini.GetBool("General", "CoverTaskbar", s.CoverTaskbar);
            s.AutoStart = ini.GetBool("General", "AutoStart", s.AutoStart);
            s.ClockShowSeconds = ini.GetBool("General", "ClockShowSeconds", s.ClockShowSeconds);
            s.ClockUse24Hour = ini.GetBool("General", "ClockUse24Hour", s.ClockUse24Hour);

            s.Normalize();
            return s;
        }

        /// <summary>修正互相冲突或越界的组合。</summary>
        public void Normalize()
        {
            if (BackgroundKind == BackgroundKind.Image && string.IsNullOrEmpty(BackgroundFileName))
            {
                BackgroundKind = BackgroundKind.SolidColor;
            }
        }

        public void Save()
        {
            Normalize();
            var ini = new IniFile();

            ini.Set("Background", "Kind", BackgroundKind.ToString());
            ini.Set("Background", "Color", ColorUtil.ToHex(BackgroundColor));
            ini.Set("Background", "File", BackgroundFileName ?? string.Empty);
            ini.Set("Background", "Brightness", BackgroundBrightness.ToString());
            ini.Set("Background", "Blur", BackgroundBlur.ToString());
            ini.Set("Background", "Fit", BackgroundFit.ToString());
            ini.Set("Background", "Dim", BackgroundDim.ToString());

            ini.Set("Appearance", "Theme", Theme.ToString());
            ini.Set("Appearance", "CardOpacity", CardOpacity.ToString());
            ini.Set("Appearance", "AccentMode", AccentMode.ToString());
            ini.Set("Appearance", "AccentColor", ColorUtil.ToHex(CustomAccent));
            ini.Set("Appearance", "CornerRadius", CornerRadius.ToString());
            ini.Set("Appearance", "ColumnRatio", ColumnRatio.ToString());
            ini.Set("Appearance", "TileGap", TileGap.ToString());
            ini.Set("Appearance", "TilePadding", TilePadding.ToString());
            ini.Set("Appearance", "ScreenMargin", ScreenMargin.ToString());

            ini.Set("Weather", "Host", WeatherHost ?? string.Empty);
            ini.Set("Weather", "Key", WeatherKey ?? string.Empty);
            ini.Set("Weather", "City", WeatherCity ?? string.Empty);
            ini.Set("Weather", "LocationId", WeatherLocationId ?? string.Empty);
            ini.Set("Weather", "CityDisplay", WeatherCityDisplay ?? string.Empty);
            ini.Set("Weather", "RefreshMinutes", WeatherRefreshMinutes.ToString());
            ini.Set("Weather", "UseCelsius", WeatherUseCelsius ? "1" : "0");

            ini.Set("Hitokoto", "RefreshMinutes", HitokotoRefreshMinutes.ToString());
            ini.Set("Hitokoto", "Categories", HitokotoCategories ?? string.Empty);

            ini.Set("Media", "RefreshSeconds", MediaRefreshSeconds.ToString());

            ini.Set("Calendar", "WeekStartsMonday", WeekStartsMonday ? "1" : "0");
            ini.Set("Calendar", "ShowLunar", ShowLunar ? "1" : "0");
            ini.Set("Calendar", "ShowSolarTerm", ShowSolarTerm ? "1" : "0");
            ini.Set("Calendar", "ShowHoliday", ShowHoliday ? "1" : "0");

            ini.Set("General", "ScreenChoice", ScreenChoice.ToString());
            ini.Set("General", "ScreenDevice", ScreenDevice ?? string.Empty);
            ini.Set("General", "CoverTaskbar", CoverTaskbar ? "1" : "0");
            ini.Set("General", "AutoStart", AutoStart ? "1" : "0");
            ini.Set("General", "ClockShowSeconds", ClockShowSeconds ? "1" : "0");
            ini.Set("General", "ClockUse24Hour", ClockUse24Hour ? "1" : "0");

            AppPaths.EnsureCreated();
            ini.Save(AppPaths.SettingsFile);
        }

        /// <summary>用于设置页的"取消"：把另一份设置的值拷回本实例。</summary>
        public void CopyFrom(AppSettings other)
        {
            BackgroundKind = other.BackgroundKind;
            BackgroundColor = other.BackgroundColor;
            BackgroundFileName = other.BackgroundFileName;
            BackgroundBrightness = other.BackgroundBrightness;
            BackgroundBlur = other.BackgroundBlur;
            BackgroundFit = other.BackgroundFit;
            BackgroundDim = other.BackgroundDim;

            Theme = other.Theme;
            CardOpacity = other.CardOpacity;
            AccentMode = other.AccentMode;
            CustomAccent = other.CustomAccent;
            CornerRadius = other.CornerRadius;
            ColumnRatio = other.ColumnRatio;
            TileGap = other.TileGap;
            TilePadding = other.TilePadding;
            ScreenMargin = other.ScreenMargin;

            WeatherHost = other.WeatherHost;
            WeatherKey = other.WeatherKey;
            WeatherCity = other.WeatherCity;
            WeatherLocationId = other.WeatherLocationId;
            WeatherCityDisplay = other.WeatherCityDisplay;
            WeatherRefreshMinutes = other.WeatherRefreshMinutes;
            WeatherUseCelsius = other.WeatherUseCelsius;

            HitokotoRefreshMinutes = other.HitokotoRefreshMinutes;
            HitokotoCategories = other.HitokotoCategories;

            MediaRefreshSeconds = other.MediaRefreshSeconds;

            WeekStartsMonday = other.WeekStartsMonday;
            ShowLunar = other.ShowLunar;
            ShowSolarTerm = other.ShowSolarTerm;
            ShowHoliday = other.ShowHoliday;

            ScreenChoice = other.ScreenChoice;
            ScreenDevice = other.ScreenDevice;
            CoverTaskbar = other.CoverTaskbar;
            AutoStart = other.AutoStart;
            ClockShowSeconds = other.ClockShowSeconds;
            ClockUse24Hour = other.ClockUse24Hour;
        }

        /// <summary>深拷贝，供设置页编辑草稿用。</summary>
        public AppSettings Clone()
        {
            var c = new AppSettings();
            c.CopyFrom(this);
            return c;
        }
    }
}
