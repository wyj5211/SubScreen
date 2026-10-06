using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using SubScreen.Core;
using SubScreen.Net;

namespace SubScreen.Ui
{
    /// <summary>
    /// 左列中间：天气（和风天气，最近三天 + 今天的详细指标）。
    /// 左侧是大图标 + 温度 + 天气现象，右侧竖排体感/湿度/风速/气压（各带图标）。
    /// </summary>
    internal sealed class WeatherTile : Tile
    {
        // 天气指标图标（码位都在 Windows 11 上实际渲染确认过）
        private static readonly IconSpec IconFeelsLike = Icons.Thermometer;
        private static readonly IconSpec IconHumidity = Icons.Humidity;
        private static readonly IconSpec IconWind = Icons.Speedometer;
        private static readonly IconSpec IconPressure = Icons.Gauge;

        private WeatherData _data;
        private string _error;
        private string _cityOverride = string.Empty;

        /// <summary>用户点了刷新按钮。</summary>
        public event Action RefreshRequested;

        /// <summary>是否已经拿到过天气数据（设置页确定后用它判断要不要立刻重新拉一次）。</summary>
        public bool HasData
        {
            get { return _data != null; }
        }

        public void SetData(WeatherData data, string cityOverride)
        {
            _data = data;
            _error = null;
            _cityOverride = cityOverride ?? string.Empty;
            MarkDirty();
        }

        public void SetError(string message)
        {
            _error = message;
            MarkDirty();
        }

        protected override void PaintTile(Graphics g, Theme theme, AppSettings settings)
        {
            PaintCardBackground(g, theme, settings);

            RectangleF c = ContentBounds(settings);
            if (c.Height < 24f || c.Width < 24f)
            {
                return;
            }

            ClearHotspots();

            if (_data == null)
            {
                PaintPlaceholder(g, theme, settings, c);
                return;
            }

            bool celsius = settings.WeatherUseCelsius;

            // ---------- 头部 ----------
            float headerH = c.Height * 0.09f;
            PaintHeader(g, theme, settings, c, headerH);

            // ---------- 下部：三天预报 ----------
            float forecastH = c.Height * 0.36f;
            float sepY = c.Bottom - forecastH - Metrics.Px(10f);
            float mainTop = c.Y + headerH + c.Height * 0.01f;
            float mainH = Math.Max(40f, sepY - mainTop);

            PaintMain(g, theme, new RectangleF(c.X, mainTop, c.Width, mainH), celsius);

            Draw.Separator(g, theme.CardBorder, c.X, sepY, c.Width);

            PaintForecast(g, theme,
                new RectangleF(c.X, sepY + Metrics.Px(10f), c.Width, Math.Max(10f, forecastH - Metrics.Px(6f))),
                celsius);
        }

        private void PaintHeader(Graphics g, Theme theme, AppSettings settings, RectangleF c, float headerH)
        {
            var header = new RectangleF(c.X, c.Y, c.Width, headerH);

            float citySize = ClampFont(headerH * 0.72f);
            Font cityFont = IconFont.GetText(citySize, FontStyle.Regular);
            string city = !string.IsNullOrEmpty(_cityOverride)
                ? _cityOverride
                : (string.IsNullOrEmpty(_data.CityName) ? settings.WeatherCity : _data.CityName);
            Draw.Text(g, city, cityFont, theme.TextPrimary, header, Draw.LeftCenter);

            float refreshSize = headerH * 0.62f;
            float refreshBoxSize = Math.Min(headerH * 0.95f, c.Width * 0.06f);
            RectangleF refreshBox = new RectangleF(
                c.Right - refreshBoxSize, header.Y + (headerH - refreshBoxSize) / 2f,
                refreshBoxSize, refreshBoxSize);
            AddHotspot(refreshBox, delegate { RaiseRefresh(); });
            PaintIconButton(g, theme, refreshBox, Icons.Refresh, refreshSize);

            string updated = "更新于 " + _data.FetchedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
            float updSize = ClampFont(headerH * 0.52f);
            Font updFont = IconFont.GetText(updSize, FontStyle.Regular);
            float updWidth = refreshBoxSize + Metrics.Px(8f) + g.MeasureString(
                updated, updFont, int.MaxValue, StringFormat.GenericTypographic).Width;
            Draw.Text(g, updated, updFont, theme.TextTertiary,
                new RectangleF(c.Right - updWidth, header.Y, updWidth - refreshBoxSize - Metrics.Px(8f), headerH),
                Draw.RightCenter);
        }

        /// <summary>左侧大图标 + 温度 + 天气现象；右侧四项指标。</summary>
        private void PaintMain(Graphics g, Theme theme, RectangleF r, bool celsius)
        {
            float leftW = r.Width * 0.44f;
            var left = new RectangleF(r.X, r.Y, leftW, r.Height);
            var right = new RectangleF(r.X + leftW + Metrics.Px(10f), r.Y,
                Math.Max(40f, r.Width - leftW - Metrics.Px(10f)), r.Height);

            // ---- 左：图标 + 温度 / 天气现象 ----
            float iconBox = Math.Min(r.Height * 0.88f, leftW * 0.30f);
            IconSpec icon = WeatherIcon.ForCode(_data.Now != null ? _data.Now.Icon : 100, false);
            Draw.Icon(g, icon, iconBox * 0.92f, theme.AccentOnCard,
                new RectangleF(left.X, left.Y + (r.Height - iconBox) / 2f, iconBox, iconBox));

            float textX = left.X + iconBox + Metrics.Px(8f);
            float textW = Math.Max(20f, left.Right - textX);

            float tempBandH = r.Height * 0.62f;
            float condBandH = Math.Max(12f, r.Height - tempBandH);

            string tempText = FormatTemp(_data.Now != null ? _data.Now.Temp : 0, celsius, true);
            float tempSize = FitFont(g, tempText, IconFont.DisplayFamily,
                tempBandH * 0.86f, textW, FontStyle.Regular, 16f);
            Font tempFont = IconFont.GetDisplay(tempSize, FontStyle.Regular);
            Draw.Text(g, tempText, tempFont, theme.TextPrimary,
                new RectangleF(textX, left.Y, textW, tempBandH), Draw.LeftCenter);

            string condition = _data.Now != null ? _data.Now.Text : string.Empty;
            if (!string.IsNullOrEmpty(condition))
            {
                Font condFont = IconFont.GetText(ClampFont(condBandH * 0.62f), FontStyle.Regular);
                Draw.Text(g, condition, condFont, theme.TextSecondary,
                    new RectangleF(textX, left.Y + tempBandH, textW, condBandH), Draw.LeftCenter);
            }

            // ---- 右：体感 / 湿度 / 风速 / 气压 ----
            PaintMetrics(g, theme, right, celsius);
        }

        private void PaintMetrics(Graphics g, Theme theme, RectangleF box, bool celsius)
        {
            WeatherNow now = _data.Now;
            if (now == null)
            {
                return;
            }

            var labels = new List<string>(4);
            var icons = new List<IconSpec>(4);

            labels.Add("体感 " + FormatTemp(now.FeelsLike, celsius, true));
            icons.Add(IconFeelsLike);

            labels.Add("湿度 " + now.Humidity + "%");
            icons.Add(IconHumidity);

            string wind = now.WindDir ?? string.Empty;
            if (!string.IsNullOrEmpty(now.WindScale))
            {
                wind = (wind + " " + now.WindScale + "级").Trim();
            }
            labels.Add(string.IsNullOrEmpty(wind) ? "风速 --" : wind);
            icons.Add(IconWind);

            labels.Add(now.Pressure > 0
                ? "气压 " + Math.Round(now.Pressure).ToString(CultureInfo.InvariantCulture) + " hPa"
                : "气压 --");
            icons.Add(IconPressure);

            float rowH = box.Height / labels.Count;
            float iconSize = Math.Min(rowH * 0.72f, box.Width * 0.085f);
            float iconCol = iconSize + Metrics.Px(10f);
            float fontSize = ClampFont(rowH * 0.50f);

            Font font = IconFont.GetText(fontSize, FontStyle.Regular);

            for (int i = 0; i < labels.Count; i++)
            {
                var row = new RectangleF(box.X, box.Y + i * rowH, box.Width, rowH);

                Draw.Icon(g, icons[i], iconSize, theme.TextTertiary,
                    new RectangleF(row.X, row.Y, iconSize, row.Height), Draw.Center);

                Draw.Text(g, labels[i], font, theme.TextSecondary,
                    new RectangleF(row.X + iconCol, row.Y, Math.Max(10f, row.Width - iconCol), row.Height),
                    Draw.LeftCenter);
            }
        }

        /// <summary>
        /// 三天预报。每一列左边是图标 + 天气情况，右边是"今天/明天/后天" + 温度区间，
        /// 左右各自贴边，中间用竖线分隔。
        /// </summary>
        private void PaintForecast(Graphics g, Theme theme, RectangleF box, bool celsius)
        {
            List<WeatherDaily> daily = _data.Daily;
            if (daily == null || daily.Count == 0)
            {
                return;
            }

            int count = Math.Min(3, daily.Count);
            float colW = box.Width / 3f;
            DateTime today = DateTime.Today;

            for (int i = 0; i < count; i++)
            {
                WeatherDaily d = daily[i];
                var col = new RectangleF(box.X + i * colW, box.Y, colW, box.Height);

                float padX = Metrics.Px(14f);
                var inner = new RectangleF(
                    col.X + padX, col.Y,
                    Math.Max(20f, col.Width - padX * 2f), col.Height);

                float leftW = inner.Width * 0.52f;
                var left = new RectangleF(inner.X, inner.Y, leftW, inner.Height);
                var right = new RectangleF(inner.X + leftW, inner.Y,
                    Math.Max(20f, inner.Width - leftW), inner.Height);

                // ---- 左：图标 + 天气情况 ----
                float iconSize = Math.Max(12f, Math.Min(inner.Height * 0.54f, leftW * 0.34f));
                Draw.Icon(g, WeatherIcon.ForCode(d.IconDay, false), iconSize, theme.AccentOnCard,
                    new RectangleF(left.X, left.Y + (left.Height - iconSize) / 2f, iconSize, iconSize));

                float textX = left.X + iconSize + Metrics.Px(9f);
                float condSize = ClampFont(inner.Height * 0.24f);
                Font condFont = IconFont.GetText(condSize, FontStyle.Regular);
                Draw.Text(g, d.TextDay, condFont, theme.TextSecondary,
                    new RectangleF(textX, left.Y, Math.Max(10f, left.Right - textX), left.Height),
                    Draw.LeftCenter);

                // ---- 右：今天 / 明天 / 后天 + 温度 ----
                string label;
                int diff = (int)(d.Date.Date - today).TotalDays;
                if (diff == 0) label = "今天";
                else if (diff == 1) label = "明天";
                else if (diff == 2) label = "后天";
                else label = d.Date.ToString("M/d", CultureInfo.InvariantCulture);

                float labelH = inner.Height * 0.44f;
                float rangeH = Math.Max(10f, inner.Height - labelH);

                Font labelFont = IconFont.GetText(ClampFont(inner.Height * 0.25f), FontStyle.Regular);
                Draw.Text(g, label, labelFont, theme.TextTertiary,
                    new RectangleF(right.X, right.Y, right.Width, labelH), Draw.RightCenter);

                string range = FormatTemp(d.TempMin, celsius, false)
                               + " ~ " + FormatTemp(d.TempMax, celsius, false);
                float rangeMax = ClampFont(inner.Height * 0.29f);
                float rangeSize = FitFont(g, range, IconFont.TextFamily, rangeMax, right.Width,
                    FontStyle.Regular, 10f);
                Font rangeFont = IconFont.GetText(rangeSize, FontStyle.Regular);
                Draw.Text(g, range, rangeFont, theme.TextPrimary,
                    new RectangleF(right.X, right.Y + labelH, right.Width, rangeH), Draw.RightCenter);

                if (i > 0)
                {
                    using (var pen = new Pen(theme.CardBorder, 1f))
                    {
                        g.DrawLine(pen, col.X, col.Y + box.Height * 0.08f, col.X, col.Bottom - box.Height * 0.08f);
                    }
                }
            }
        }

        private void PaintPlaceholder(Graphics g, Theme theme, AppSettings settings, RectangleF c)
        {
            string title;
            string subtitle;

            if (!string.IsNullOrEmpty(_error))
            {
                title = "天气获取失败";
                subtitle = _error;
            }
            else if (string.IsNullOrEmpty(settings.WeatherHost) || string.IsNullOrEmpty(settings.WeatherKey))
            {
                title = "天气未配置";
                subtitle = "请在设置里填写和风天气的 API Host 与 Key";
            }
            else
            {
                title = "正在获取天气…";
                subtitle = settings.WeatherCity;
            }

            // 图标 + 两行文字整块垂直居中，避免上半部挤在一起、下半部大片留白
            float iconBox = Math.Min(c.Height * 0.34f, c.Width * 0.16f);
            float titleSize = ClampFont(Math.Min(c.Height * 0.105f, iconBox * 0.42f));
            float subSize = ClampFont(Math.Min(c.Height * 0.066f, iconBox * 0.26f));

            Font titleFont = IconFont.GetText(titleSize, FontStyle.Regular);
            Font subFont = IconFont.GetText(subSize, FontStyle.Regular);

            SizeF subMeasured = g.MeasureString(subtitle, subFont,
                (int)Math.Max(40f, c.Width - iconBox - Metrics.Px(14f)), StringFormat.GenericDefault);
            float textBlockH = titleSize * 1.45f + Math.Min(subMeasured.Height, subSize * 4.2f);
            float blockH = Math.Max(iconBox, textBlockH);
            float blockY = c.Y + (c.Height - blockH) * 0.5f;

            Draw.Icon(g, Icons.Cloud, iconBox * 0.82f, theme.TextTertiary,
                new RectangleF(c.X, blockY + (blockH - iconBox) / 2f, iconBox, iconBox), Draw.Center);

            float textX = c.X + iconBox + Metrics.Px(14f);
            float textW = Math.Max(40f, c.Right - textX);
            float titleH = titleSize * 1.45f;

            Draw.Text(g, title, titleFont, theme.TextPrimary,
                new RectangleF(textX, blockY, textW, titleH), Draw.LeftCenter);

            Draw.Text(g, subtitle, subFont, theme.TextTertiary,
                new RectangleF(textX, blockY + titleH, textW, Math.Max(subSize * 1.4f, blockH - titleH)),
                Draw.WrapLeft);

            // 仍然允许手动刷新
            float refreshBoxSize = Math.Min(c.Height * 0.14f, c.Width * 0.06f);
            RectangleF refreshBox = new RectangleF(
                c.Right - refreshBoxSize, c.Y, refreshBoxSize, refreshBoxSize);
            AddHotspot(refreshBox, delegate { RaiseRefresh(); });
            PaintIconButton(g, theme, refreshBox, Icons.Refresh, refreshBoxSize * 0.62f);
        }

        private void RaiseRefresh()
        {
            Action handler = RefreshRequested;
            if (handler != null)
            {
                handler();
            }
        }

        private static string FormatTemp(double celsius, bool useCelsius, bool withUnit)
        {
            double value = useCelsius ? celsius : celsius * 9.0 / 5.0 + 32.0;
            string s = Math.Round(value) + "°";
            if (withUnit)
            {
                s += useCelsius ? "C" : "F";
            }
            return s;
        }

        /// <summary>二分收敛到一个能塞进宽度的字号，避免长文本被裁切。</summary>
        private static float FitFont(
            Graphics g, string text, string family, float desired, float maxWidth, FontStyle style, float minSize)
        {
            if (string.IsNullOrEmpty(text) || desired < minSize)
            {
                return Math.Max(minSize, desired);
            }

            float size = desired;
            for (int i = 0; i < 12 && size > minSize; i++)
            {
                using (var f = new Font(family, size, style, GraphicsUnit.Pixel))
                {
                    SizeF m = g.MeasureString(text, f, int.MaxValue, StringFormat.GenericTypographic);
                    if (m.Width <= maxWidth)
                    {
                        return size;
                    }
                }
                size *= 0.92f;
            }
            return Math.Max(minSize, size);
        }

        private static float ClampFont(float v)
        {
            if (v < 8f) return 8f;
            if (v > 200f) return 200f;
            return v;
        }
    }
}
