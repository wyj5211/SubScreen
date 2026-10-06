using System;
using System.Globalization;

namespace SubScreen.Calendar
{
    /// <summary>
    /// 农历日期信息（纯数据载体，不含任何逻辑）。
    /// 由 <see cref="LunarCalendarService.Get"/> 生成，供日历渲染层直接读取。
    /// </summary>
    public sealed class LunarDateInfo
    {
        /// <summary>农历年（干支纪年所对应的数字年份，例如 2025 表示乙巳年）。</summary>
        public int LunarYear;

        /// <summary>农历月 1-12。闰月时仍然等于实际的月份数（闰六月 → 6）。</summary>
        public int LunarMonth;

        /// <summary>农历日 1-30。</summary>
        public int LunarDay;

        /// <summary>是否闰月。</summary>
        public bool IsLeapMonth;

        /// <summary>月名："正月" "二月" ... "十月" "冬月" "腊月"（按约定不加"闰"字前缀）。</summary>
        public string MonthName;

        /// <summary>日名："初一" ... "初十" "十一" ... "二十" "廿一" ... "三十"。</summary>
        public string DayName;

        /// <summary>生肖："鼠" "牛" "虎" "兔" "龙" "蛇" "马" "羊" "猴" "鸡" "狗" "猪"。</summary>
        public string Zodiac;

        /// <summary>干支纪年，如 "乙巳"。</summary>
        public string GanZhiYear;

        /// <summary>短文本：例 "腊月初八"；若为初一则只显示月名，如 "正月"。</summary>
        public string ShortText;
    }

    /// <summary>
    /// 农历转换服务。内部使用 .NET 框架自带的 <see cref="ChineseLunisolarCalendar"/>，
    /// 不自行实现任何天文算法，保证 1901-2100 年范围内的结果与框架数据一致。
    /// </summary>
    public static class LunarCalendarService
    {
        // ---------------------------------------------------------------- 常量

        /// <summary>支持的最早公历年份。</summary>
        public const int MinYear = 1901;

        /// <summary>支持的最晚公历年份。</summary>
        public const int MaxYear = 2100;

        private const string GanChars = "甲乙丙丁戊己庚辛壬癸"; // 天干，10 个
        private const string ZhiChars = "子丑寅卯辰巳午未申酉戌亥"; // 地支，12 个
        private const string DayDigits = "一二三四五六七八九十"; // 日名用中文数字

        /// <summary>月名表，下标 1-12 有效（下标 0 占位，永不使用）。</summary>
        private static readonly string[] MonthNames =
        {
            string.Empty,
            "正月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "冬月", "腊月"
        };

        /// <summary>生肖表，下标 0-11：(农历年 - 4) % 12。</summary>
        private static readonly string[] Zodiacs =
        {
            "鼠", "牛", "虎", "兔", "龙", "蛇",
            "马", "羊", "猴", "鸡", "狗", "猪"
        };

        /// <summary>日名表，下标 1-30 有效（下标 0 占位）。</summary>
        private static readonly string[] DayNameTable = BuildDayNameTable();

        // 日历实例是只读的（其内部只有查表数据），可安全共享；
        // 本类所有公开方法均不修改实例状态。
        private static readonly ChineseLunisolarCalendar Cal = new ChineseLunisolarCalendar();

        // ---------------------------------------------------------------- 公开 API

        /// <summary>
        /// 取得指定公历日期对应的农历信息。
        /// 仅支持 <see cref="MinYear"/>-<see cref="MaxYear"/>（1901-2100）年；
        /// 越界或框架数据不支持时安全返回 null，不抛异常。
        /// </summary>
        public static LunarDateInfo Get(DateTime date)
        {
            // 年份越界：按需求直接返回 null
            if (date.Year < MinYear || date.Year > MaxYear)
            {
                return null;
            }

            DateTime d = date.Date;

            // ChineseLunisolarCalendar 的有效区间约为 1901-02-19 ~ 2101-01-28，
            // 早于该区间的 1901 年 1-2 月日期必须提前挡掉，否则框架会抛异常。
            if (d < Cal.MinSupportedDateTime.Date || d > Cal.MaxSupportedDateTime.Date)
            {
                return null;
            }

            try
            {
                int lunarYear = Cal.GetYear(d);

                // GetMonth 返回的是"含闰月的序号"（1-13），需要按闰月位置折算成真实月份。
                int month = Cal.GetMonth(d);
                int day = Cal.GetDayOfMonth(d);
                int leap = Cal.GetLeapMonth(lunarYear);

                bool isLeap = false;
                if (leap != 0 && month >= leap)
                {
                    // month == leap 即闰月本身；month > leap 说明该月之前插了一个闰月，需要减 1。
                    isLeap = (month == leap);
                    month = month - 1;
                }

                if (month < 1 || month > 12 || day < 1 || day > 30)
                {
                    // 理论上不会发生，仅作兜底，避免脏数据流入界面
                    return null;
                }

                string monthName = MonthNames[month];
                string dayName = DayNameTable[day];

                LunarDateInfo info = new LunarDateInfo();
                info.LunarYear = lunarYear;
                info.LunarMonth = month;
                info.LunarDay = day;
                info.IsLeapMonth = isLeap;
                info.MonthName = monthName;
                info.DayName = dayName;
                info.Zodiac = GetZodiac(lunarYear);
                info.GanZhiYear = GetGanZhi(d);
                // 初一只显示月名（如"正月"），其余显示"月名 + 日名"（如"腊月初八"）
                info.ShortText = (day == 1) ? monthName : (monthName + dayName);
                return info;
            }
            catch (ArgumentOutOfRangeException)
            {
                // 框架历法上下界之外，安全返回 null
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- 内部实现

        /// <summary>生肖：(农历年 - 4) % 12 映射到"鼠牛虎兔龙蛇马羊猴鸡狗猪"。</summary>
        private static string GetZodiac(int lunarYear)
        {
            int index = (lunarYear - 4) % 12;
            if (index < 0)
            {
                index += 12; // 兜底：正常范围内不会为负
            }
            return Zodiacs[index];
        }

        /// <summary>干支纪年：由六十甲子序号分别取天干、地支。</summary>
        private static string GetGanZhi(DateTime date)
        {
            int sexagenaryYear = Cal.GetSexagenaryYear(date);
            int stem = Cal.GetCelestialStem(sexagenaryYear);      // 1-10
            int branch = Cal.GetTerrestrialBranch(sexagenaryYear); // 1-12

            if (stem < 1 || stem > 10 || branch < 1 || branch > 12)
            {
                return string.Empty; // 兜底
            }

            return GanChars[stem - 1].ToString() + ZhiChars[branch - 1].ToString();
        }

        /// <summary>生成 1-30 的日名查表数据。</summary>
        private static string[] BuildDayNameTable()
        {
            string[] table = new string[31];
            table[0] = string.Empty; // 占位，农历日从 1 开始
            for (int day = 1; day <= 30; day++)
            {
                table[day] = BuildDayName(day);
            }
            return table;
        }

        /// <summary>
        /// 中文日名：1→初一 ... 10→初十，11→十一 ... 19→十九，
        /// 20→二十，21→廿一 ... 29→廿九，30→三十。
        /// </summary>
        private static string BuildDayName(int day)
        {
            if (day <= 0 || day > 30)
            {
                return string.Empty; // 兜底
            }
            if (day <= 10)
            {
                // 初一 ... 初十
                return "初" + DayDigits[day - 1];
            }
            if (day < 20)
            {
                // 十一 ... 十九
                return "十" + DayDigits[day - 11];
            }
            if (day == 20)
            {
                return "二十";
            }
            if (day < 30)
            {
                // 廿一 ... 廿九
                return "廿" + DayDigits[day - 21];
            }
            return "三十";
        }
    }
}
