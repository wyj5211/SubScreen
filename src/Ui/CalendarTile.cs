using System;
using System.Drawing;
using System.Globalization;
using SubScreen.Calendar;
using SubScreen.Core;

namespace SubScreen.Ui
{
    /// <summary>右下角：日历。风格参考 Windows 11 系统托盘日历。</summary>
    internal sealed class CalendarTile : Tile
    {
        private const int Columns = 7;
        private const int Rows = 6;

        private struct DayCell
        {
            public DateTime Date;
            public bool InMonth;
            public string Sub;
            public bool SubIsFestival;
        }

        private readonly DayCell[] _cells = new DayCell[Columns * Rows];

        private DateTime _today = DateTime.Today;
        private DateTime _displayMonth;
        private DateTime _selected = DateTime.Today;
        private bool _cellsDirty = true;

        public CalendarTile()
        {
            _displayMonth = new DateTime(_today.Year, _today.Month, 1);
        }

        /// <summary>跨天时调用。</summary>
        public bool Tick()
        {
            DateTime now = DateTime.Today;
            if (now == _today)
            {
                return false;
            }

            bool wasOnToday = _selected == _today;
            _today = now;
            if (wasOnToday)
            {
                _selected = now;
            }
            _cellsDirty = true;
            MarkDirty();
            return true;
        }

        private void EnsureCells()
        {
            if (!_cellsDirty)
            {
                return;
            }

            bool mondayFirst = CurrentSettings == null || CurrentSettings.WeekStartsMonday;
            bool showLunar = CurrentSettings == null || CurrentSettings.ShowLunar;
            bool showTerm = CurrentSettings == null || CurrentSettings.ShowSolarTerm && CurrentSettings.ShowHoliday;

            var first = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            int shift = mondayFirst ? 1 : 0;
            int offset = ((int)first.DayOfWeek - shift + 7) % 7;
            DateTime start = first.AddDays(-offset);

            for (int i = 0; i < _cells.Length; i++)
            {
                DateTime date = start.AddDays(i);

                DayCell cell = new DayCell();
                cell.Date = date;
                cell.InMonth = date.Month == _displayMonth.Month;

                // 优先顺序：法定节假日 > 节气 > 农历月首 > 农历日
                HolidayKind kind = (CurrentSettings == null || CurrentSettings.ShowHoliday)
                    ? ChineseHolidays.GetHolidayKind(date)
                    : HolidayKind.None;

                if (kind == HolidayKind.Legal)
                {
                    cell.Sub = ChineseHolidays.GetFestival(date);
                    cell.SubIsFestival = true;
                }
                else
                {
                    string term = showTerm ? ChineseHolidays.GetSolarTerm(date) : null;
                    if (!string.IsNullOrEmpty(term))
                    {
                        cell.Sub = term;
                    }
                    else if (showLunar)
                    {
                        LunarDateInfo lunar = LunarCalendarService.Get(date);
                        if (lunar != null)
                        {
                            cell.Sub = lunar.LunarDay == 1
                                ? (lunar.IsLeapMonth ? "闰" : string.Empty) + lunar.MonthName
                                : lunar.DayName;
                        }
                    }
                }

                _cells[i] = cell;
            }

            _cellsDirty = false;
        }

        protected override void PaintTile(Graphics g, Theme theme, AppSettings settings)
        {
            PaintCardBackground(g, theme, settings);
            EnsureCells();

            RectangleF c = ContentBounds(settings);
            if (c.Height < 40f || c.Width < 60f)
            {
                return;
            }

            ClearHotspots();

            // ---------- 头部 ----------
            float headerH = c.Height * 0.085f;
            var header = new RectangleF(c.X, c.Y, c.Width, headerH);

            float titleSize = ClampFont(headerH * 0.60f);
            Font titleFont = IconFont.GetText(titleSize, FontStyle.Regular);
            string title = string.Format(
                CultureInfo.InvariantCulture, "{0}年{1}月", _displayMonth.Year, _displayMonth.Month);

            var titleRect = new RectangleF(header.X, header.Y, header.Width * 0.6f, header.Height);
            Draw.Text(g, title, titleFont, theme.TextPrimary, titleRect, Draw.LeftCenter);
            AddHotspot(titleRect, delegate { GoToToday(); });

            float chevSize = Math.Min(headerH * 0.85f, c.Width * 0.045f);
            float cx = header.Right - chevSize;
            var nextBox = new RectangleF(cx, header.Y + (headerH - chevSize) / 2f, chevSize, chevSize);
            cx -= chevSize + Metrics.Px(4f);
            var prevBox = new RectangleF(cx, header.Y + (headerH - chevSize) / 2f, chevSize, chevSize);

            AddHotspot(prevBox, delegate { ShiftMonth(-1); });
            AddHotspot(nextBox, delegate { ShiftMonth(1); });
            PaintIconButton(g, theme, prevBox, Icons.ChevronLeft, chevSize * 0.5f);
            PaintIconButton(g, theme, nextBox, Icons.ChevronRight, chevSize * 0.5f);

            // 不在当月时给一个"今天"快捷入口
            bool onCurrentMonth = _displayMonth.Year == _today.Year && _displayMonth.Month == _today.Month;
            if (!onCurrentMonth)
            {
                float btnW = headerH * 1.5f;
                var todayBox = new RectangleF(prevBox.X - Metrics.Px(8f) - btnW,
                    header.Y + (headerH - chevSize) / 2f, btnW, chevSize);
                if (todayBox.X > titleRect.Right)
                {
                    AddHotspot(todayBox, delegate { GoToToday(); });
                    Font btnFont = IconFont.GetText(ClampFont(chevSize * 0.52f), FontStyle.Regular);
                    bool hover = IsHovered(todayBox);
                    if (hover)
                    {
                        Draw.FillRounded(g, ColorUtil.WithAlpha(theme.TextPrimary, 26), todayBox, chevSize / 2f);
                    }
                    Draw.Text(g, "今天", btnFont, hover ? theme.TextPrimary : theme.AccentOnCard,
                        todayBox, Draw.Center);
                }
            }

            // ---------- 星期表头 ----------
            float weekH = c.Height * 0.058f;
            var week = new RectangleF(c.X, header.Bottom + c.Height * 0.012f, c.Width, weekH);
            float colW = c.Width / Columns;

            bool mondayFirst = settings.WeekStartsMonday;
            string[] weekNames = mondayFirst
                ? new[] { "一", "二", "三", "四", "五", "六", "日" }
                : new[] { "日", "一", "二", "三", "四", "五", "六" };

            Font weekFont = IconFont.GetText(ClampFont(weekH * 0.68f), FontStyle.Regular);
            for (int i = 0; i < Columns; i++)
            {
                Draw.Text(g, weekNames[i], weekFont, theme.TextTertiary,
                    new RectangleF(week.X + i * colW, week.Y, colW, weekH), Draw.Center);
            }

            // ---------- 日期网格 ----------
            float gridTop = week.Bottom + c.Height * 0.010f;
            float gridH = c.Bottom - gridTop;
            if (gridH < 20f)
            {
                return;
            }
            float rowH = gridH / Rows;

            float numberSize = ClampFont(Math.Min(rowH * 0.34f, colW * 0.30f));
            float subSize = ClampFont(Math.Min(rowH * 0.20f, colW * 0.17f));
            Font numberFont = IconFont.GetText(numberSize, FontStyle.Regular);
            Font subFont = IconFont.GetText(subSize, FontStyle.Regular);

            float markSize = Math.Min(rowH * 0.58f, colW * 0.42f);

            Color normalNumber = theme.TextPrimary;
            Color dimNumber = ColorUtil.WithAlpha(theme.TextPrimary, 90);
            Color normalSub = theme.TextTertiary;
            Color dimSub = ColorUtil.WithAlpha(theme.TextTertiary, 70);

            for (int i = 0; i < _cells.Length; i++)
            {
                DayCell cell = _cells[i];
                int col = i % Columns;
                int row = i / Columns;

                var box = new RectangleF(c.X + col * colW, gridTop + row * rowH, colW, rowH);
                bool isToday = cell.Date == _today;
                bool isSelected = cell.Date == _selected;

                // 数字底圈：今天实心、选中描边
                var mark = new RectangleF(
                    box.X + (box.Width - markSize) / 2f,
                    box.Y + rowH * 0.06f,
                    markSize, markSize);

                if (isToday)
                {
                    // Windows 11 托盘日历里"今天"是强调色实心圆
                    Draw.FillRounded(g, theme.Accent, mark, markSize * 0.5f);
                }
                else if (isSelected)
                {
                    Draw.DrawRoundedBorder(g, theme.AccentOnCard, mark, markSize * 0.5f,
                        Math.Max(1.5f, Metrics.Px(1.8f)));
                }

                Color numColor = isToday
                    ? theme.OnAccent
                    : (cell.InMonth ? normalNumber : dimNumber);

                var numRect = new RectangleF(box.X, mark.Y, box.Width, mark.Height);
                Draw.Text(g, cell.Date.Day.ToString(CultureInfo.InvariantCulture), numberFont, numColor,
                    numRect, Draw.Center);

                if (!string.IsNullOrEmpty(cell.Sub))
                {
                    Color subColor;
                    if (cell.SubIsFestival)
                    {
                        subColor = theme.AccentOnCard;
                    }
                    else
                    {
                        subColor = cell.InMonth ? normalSub : dimSub;
                    }

                    var subRect = new RectangleF(
                        box.X, mark.Bottom + rowH * 0.02f, box.Width, Math.Max(6f, rowH * 0.26f));
                    Draw.Text(g, cell.Sub, subFont, subColor, subRect, Draw.Center);
                }

                DateTime clicked = cell.Date;
                AddHotspot(box, delegate { SelectDate(clicked); });
            }
        }

        private void SelectDate(DateTime date)
        {
            _selected = date;
            if (date.Month != _displayMonth.Month || date.Year != _displayMonth.Year)
            {
                _displayMonth = new DateTime(date.Year, date.Month, 1);
                _cellsDirty = true;
            }
            MarkDirty();
        }

        /// <summary>仅供截图自检：预设一个选中日期，好把"选中描边"也画出来。</summary>
        internal void DemoSelect(DateTime date)
        {
            SelectDate(date);
        }

        private void ShiftMonth(int delta)
        {
            _displayMonth = _displayMonth.AddMonths(delta);
            _cellsDirty = true;
            MarkDirty();
        }

        private void GoToToday()
        {
            _today = DateTime.Today;
            _selected = _today;
            _displayMonth = new DateTime(_today.Year, _today.Month, 1);
            _cellsDirty = true;
            MarkDirty();
        }

        private static float ClampFont(float v)
        {
            if (v < 7f) return 7f;
            if (v > 200f) return 200f;
            return v;
        }
    }
}
