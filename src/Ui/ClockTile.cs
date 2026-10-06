using System;
using System.Drawing;
using System.Globalization;
using SubScreen.Calendar;
using SubScreen.Core;

namespace SubScreen.Ui
{
    /// <summary>左上角：当前时间 + 年月日 + 星期 + 农历。</summary>
    internal sealed class ClockTile : Tile
    {
        private static readonly string[] WeekNames =
        {
            "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"
        };

        private string _timeText = string.Empty;
        private string _dateText = string.Empty;
        private string _lunarText = string.Empty;
        private string _festivalText = string.Empty;

        private DateTime _lastDate = DateTime.MinValue;

        /// <summary>
        /// 用当前时间刷新显示内容。返回 true 表示显示的文本变了（需要重绘）。
        /// 农历/节日只在跨天时重算，避免每秒都走一次农历历法。
        /// </summary>
        public bool Update(DateTime now)
        {
            bool changed = false;

            if (now.Date != _lastDate)
            {
                _lastDate = now.Date;
                changed |= RecomputeDateParts(now);
            }

            string time = FormatTime(now);
            if (time != _timeText)
            {
                _timeText = time;
                changed = true;
            }

            if (changed)
            {
                MarkDirty();
            }
            return changed;
        }

        private string FormatTime(DateTime now)
        {
            bool showSeconds = CurrentSettings != null && CurrentSettings.ClockShowSeconds;
            bool use24 = CurrentSettings == null || CurrentSettings.ClockUse24Hour;

            if (use24)
            {
                return showSeconds
                    ? now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    : now.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            string ampm = now.Hour < 12 ? "上午" : "下午";
            string fmt = showSeconds ? "h:mm:ss" : "h:mm";
            return ampm + " " + now.ToString(fmt, CultureInfo.InvariantCulture);
        }

        private bool RecomputeDateParts(DateTime now)
        {
            string date = string.Format(
                CultureInfo.InvariantCulture,
                "{0}年{1}月{2}日  {3}",
                now.Year, now.Month, now.Day, WeekNames[(int)now.DayOfWeek]);

            string lunar = string.Empty;
            LunarDateInfo info = LunarCalendarService.Get(now);
            if (info != null)
            {
                lunar = string.Format(
                    CultureInfo.InvariantCulture,
                    "农历{0}{1}",
                    info.IsLeapMonth ? "闰" : string.Empty,
                    info.ShortText);

                if (!string.IsNullOrEmpty(info.GanZhiYear))
                {
                    lunar += " · " + info.GanZhiYear + info.Zodiac + "年";
                }
            }

            string festival = ChineseHolidays.GetFestival(now) ?? string.Empty;

            bool changed = date != _dateText || lunar != _lunarText || festival != _festivalText;
            _dateText = date;
            _lunarText = lunar;
            _festivalText = festival;
            return changed;
        }

        protected override void PaintTile(Graphics g, Theme theme, AppSettings settings)
        {
            PaintCardBackground(g, theme, settings);

            RectangleF c = ContentBounds(settings);
            if (c.Height < 20f || c.Width < 20f)
            {
                return;
            }

            ClearHotspots();

            // 竖向排布：时间 / 日期+星期 / 农历
            float timeH = c.Height * 0.50f;
            float dateH = c.Height * 0.20f;
            float lunarH = c.Height * 0.16f;
            float gap = c.Height * 0.03f;

            float y = c.Y;

            if (!string.IsNullOrEmpty(_timeText))
            {
                float fontSize = FitFont(g, _timeText, IconFont.DisplayFamily, timeH * 0.92f, c.Width, FontStyle.Regular, 24f);
                Font font = IconFont.GetDisplay(fontSize, FontStyle.Regular);
                Draw.Text(g, _timeText, font, theme.TextPrimary,
                    new RectangleF(c.X, y, c.Width, timeH), Draw.LeftCenter);
            }
            y += timeH + gap;

            if (!string.IsNullOrEmpty(_dateText))
            {
                float fontSize = FitFont(g, _dateText, IconFont.TextFamily, dateH * 0.82f, c.Width, FontStyle.Regular, 11f);
                Font font = IconFont.GetText(fontSize, FontStyle.Regular);
                Draw.Text(g, _dateText, font, theme.TextSecondary,
                    new RectangleF(c.X, y, c.Width, dateH), Draw.LeftCenter);
            }
            y += dateH + gap;

            // 农历在左，节日（若有）用强调色跟在右边
            if (!string.IsNullOrEmpty(_lunarText))
            {
                float fontSize = FitFont(g, _lunarText, IconFont.TextFamily, lunarH * 0.82f, c.Width, FontStyle.Regular, 10f);
                Font font = IconFont.GetText(fontSize, FontStyle.Regular);

                if (string.IsNullOrEmpty(_festivalText))
                {
                    Draw.Text(g, _lunarText, font, theme.TextTertiary,
                        new RectangleF(c.X, y, c.Width, lunarH), Draw.LeftCenter);
                }
                else
                {
                    SizeF measured = Draw.Measure(g, _lunarText, font);
                    Draw.Text(g, _lunarText, font, theme.TextTertiary,
                        new RectangleF(c.X, y, c.Width, lunarH), Draw.LeftCenter);

                    float badgeX = c.X + measured.Width + Metrics.Px(10f);
                    Font badgeFont = IconFont.GetText(fontSize, FontStyle.Regular);
                    Draw.Text(g, _festivalText, badgeFont, theme.AccentOnCard,
                        new RectangleF(badgeX, y, Math.Max(10f, c.Right - badgeX), lunarH), Draw.LeftCenter);
                }
            }
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
    }
}
