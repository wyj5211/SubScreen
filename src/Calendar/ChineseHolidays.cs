using System;

namespace SubScreen.Calendar
{
    /// <summary>节日类型，用于决定日历中文字是否使用强调色。</summary>
    public enum HolidayKind
    {
        /// <summary>普通日期，无节日。</summary>
        None = 0,

        /// <summary>法定放假节日（元旦、除夕、春节、清明节、劳动节、端午节、中秋节、国庆节）。</summary>
        Legal = 1,

        /// <summary>传统节日，但按本类的口径不算"真正放假"（元宵、龙抬头、七夕、中元、重阳、腊八）。</summary>
        Traditional = 2
    }

    /// <summary>
    /// 中国节日与二十四节气查询服务。纯逻辑、无外部依赖、无外部数据文件。
    /// 说明：本类判断的是"日期本身是否属于某个节日"，
    /// 不判断国务院每年公布的调休/连休安排（那种安排逐年变化，必须靠外部数据）。
    /// </summary>
    public static class ChineseHolidays
    {
        // ================================================================
        // 二十四节气（寿星公式 / 通用通式）
        //   日期 = (int)(Y * 0.2422 + C) - (int)(Y / 4.0)
        //   Y = 公历年份的后两位，C = 该节气在 21 世纪的世纪常数。
        //   本表为"21 世纪（2001-2100）"常数；20 世纪常数不同，故 2000 年及以前不判断，
        //   返回 null（宁可不给，也不给错）。
        // ================================================================

        /// <summary>节气可计算的最早年份。</summary>
        public const int SolarTermMinYear = 2001;

        /// <summary>节气可计算的最晚年份（21 世纪常数适用范围）。</summary>
        public const int SolarTermMaxYear = 2100;

        // 节气下标：按公历月份顺序，每月两个
        private const int TermXiaoHan = 0;  // 小寒  1月
        private const int TermDaHan = 1;    // 大寒  1月
        private const int TermLiChun = 2;   // 立春  2月
        private const int TermYuShui = 3;   // 雨水  2月
        private const int TermJingZhe = 4;  // 惊蛰  3月
        private const int TermChunFen = 5;  // 春分  3月
        private const int TermQingMing = 6; // 清明  4月
        private const int TermGuYu = 7;     // 谷雨  4月
        private const int TermLiXia = 8;    // 立夏  5月
        private const int TermXiaoMan = 9;  // 小满  5月
        private const int TermMangZhong = 10; // 芒种 6月
        private const int TermXiaZhi = 11;  // 夏至  6月
        private const int TermXiaoShu = 12; // 小暑  7月
        private const int TermDaShu = 13;   // 大暑  7月
        private const int TermLiQiu = 14;   // 立秋  8月
        private const int TermChuShu = 15;  // 处暑  8月
        private const int TermBaiLu = 16;   // 白露  9月
        private const int TermQiuFen = 17;  // 秋分  9月
        private const int TermHanLu = 18;   // 寒露 10月
        private const int TermShuangJiang = 19; // 霜降 10月
        private const int TermLiDong = 20;  // 立冬 11月
        private const int TermXiaoXue = 21; // 小雪 11月
        private const int TermDaXue = 22;   // 大雪 12月
        private const int TermDongZhi = 23; // 冬至 12月

        /// <summary>24 个节气名，下标与上面的 Term* 常量一致。</summary>
        private static readonly string[] TermNames =
        {
            "小寒", "大寒", "立春", "雨水", "惊蛰", "春分",
            "清明", "谷雨", "立夏", "小满", "芒种", "夏至",
            "小暑", "大暑", "立秋", "处暑", "白露", "秋分",
            "寒露", "霜降", "立冬", "小雪", "大雪", "冬至"
        };

        /// <summary>24 个节气所属公历月份（每月两个，顺序同上）。</summary>
        private static readonly int[] TermMonths =
        {
            1, 1, 2, 2, 3, 3,
            4, 4, 5, 5, 6, 6,
            7, 7, 8, 8, 9, 9,
            10, 10, 11, 11, 12, 12
        };

        /// <summary>21 世纪（2001-2100）各节气的世纪常数 C。</summary>
        private static readonly double[] TermConstants =
        {
            5.4055, // 小寒
            20.12,  // 大寒
            3.87,   // 立春
            18.73,  // 雨水
            5.63,   // 惊蛰
            20.646, // 春分
            4.81,   // 清明（与需求中给出的清明公式一致）
            20.1,   // 谷雨
            5.52,   // 立夏
            21.04,  // 小满
            5.678,  // 芒种
            21.37,  // 夏至
            7.108,  // 小暑
            22.83,  // 大暑
            7.5,    // 立秋
            23.13,  // 处暑
            7.646,  // 白露
            23.042, // 秋分
            8.318,  // 寒露
            23.438, // 霜降
            7.438,  // 立冬
            22.36,  // 小雪
            7.18,   // 大雪
            21.94   // 冬至
        };

        /// <summary>
        /// 寿星公式的"特殊年份例外修正"。
        /// 只收录能够交叉验证的条目（闰年/平年切换导致公式在跨日边界上差 1 天）。
        /// 每条都在注释里写明依据；无法确认的已公开例外（如 2002 年立秋、2008 年小满、
        /// 2016 年小暑、2082 年大寒等）没有收录，宁可少也不要写错。
        /// </summary>
        private struct TermException
        {
            public readonly int Year;      // 公历年份（2001-2100）
            public readonly int TermIndex; // 节气下标
            public readonly int Delta;     // 日期修正量（+1 / -1）

            public TermException(int year, int termIndex, int delta)
            {
                Year = year;
                TermIndex = termIndex;
                Delta = delta;
            }
        }

        private static readonly TermException[] TermExceptions =
        {
            // 2019 年小寒：公式给 1 月 6 日，实际为 2019-01-05。相邻年份（2018/2020/2021）
            // 公式均正确给出 1 月 5 日，可确认是 2019 年单独的例外。
            new TermException(2019, TermXiaoHan, -1),

            // 2021 年冬至：公式给 12 月 22 日，实际为 2021-12-21（北京时间 23:59，临界）。
            // 相邻年份（2022 与 2023 公式给 12 月 22 日、2024/2025 给 12 月 21 日）均与实际相符。
            new TermException(2021, TermDongZhi, -1),

            // 2026 年雨水：公式给 2 月 19 日，实际为 2026-02-18（北京时间约 23:55，临界）。
            // 由 2025 年雨水（2025-02-18 18:06）+ 一个回归年 365.2422 天推算即落在 2 月 18 日深夜；
            // 相邻年份 2025（2/18）与 2027（2/19）公式本身正确。
            new TermException(2026, TermYuShui, -1)
        };

        // ================================================================
        // 公开 API
        // ================================================================

        /// <summary>
        /// 当天最主要的节日名；没有节日返回 null。
        /// 法定放假节日（元旦、除夕、春节、清明节、劳动节、端午节、中秋节、国庆节）
        /// 优先于其他传统节日。
        /// </summary>
        public static string GetFestival(DateTime date)
        {
            HolidayKind kind;
            return Resolve(date, out kind);
        }

        /// <summary>
        /// 是否是"真正放假"的法定节假日：元旦(1/1)、除夕、春节(正月初一)、清明节、
        /// 劳动节(5/1)、端午节(五月初五)、中秋节(八月十五)、国庆节(10/1)。
        /// 注意：这里只按节日日期判断，不反映逐年公布的调休安排。
        /// </summary>
        public static bool IsLegalHoliday(DateTime date)
        {
            return GetLegalFestival(date) != null;
        }

        /// <summary>节日类型，用于决定日历里文字是否用强调色。</summary>
        public static HolidayKind GetHolidayKind(DateTime date)
        {
            HolidayKind kind;
            Resolve(date, out kind);
            return kind;
        }

        /// <summary>
        /// 二十四节气名（如 "清明"、"冬至"）；当天不是节气则返回 null。
        /// 仅 2001-2100 年可算（21 世纪常数），此范围外返回 null。
        /// </summary>
        public static string GetSolarTerm(DateTime date)
        {
            if (date.Year < SolarTermMinYear || date.Year > SolarTermMaxYear)
            {
                return null;
            }

            for (int i = 0; i < TermNames.Length; i++)
            {
                if (TermMonths[i] != date.Month)
                {
                    continue;
                }
                if (GetSolarTermDay(date.Year, i) == date.Day)
                {
                    return TermNames[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 取某年某节气的公历日期（当月第几天）。仅 2001-2100 年有效。
        /// 公开出来便于上层做"未来节气提醒"之类的功能。
        /// </summary>
        public static int GetSolarTermDay(int year, int termIndex)
        {
            if (year < SolarTermMinYear || year > SolarTermMaxYear)
            {
                return 0;
            }
            if (termIndex < 0 || termIndex >= TermNames.Length)
            {
                return 0;
            }

            int y = year % 100;
            double raw = y * 0.2422 + TermConstants[termIndex];
            int day = (int)raw - (int)(y / 4.0);
            day += GetTermExceptionDelta(year, termIndex);
            return day;
        }

        /// <summary>取某年中第 termIndex 个节气的名称（供上层展示用）。</summary>
        public static string GetSolarTermName(int termIndex)
        {
            if (termIndex < 0 || termIndex >= TermNames.Length)
            {
                return null;
            }
            return TermNames[termIndex];
        }

        // ================================================================
        // 内部实现
        // ================================================================

        /// <summary>统一解析：返回节日名并输出类型（法定优先于传统）。</summary>
        private static string Resolve(DateTime date, out HolidayKind kind)
        {
            string legal = GetLegalFestival(date);
            if (legal != null)
            {
                kind = HolidayKind.Legal;
                return legal;
            }

            string traditional = GetTraditionalFestival(date);
            if (traditional != null)
            {
                kind = HolidayKind.Traditional;
                return traditional;
            }

            kind = HolidayKind.None;
            return null;
        }

        /// <summary>法定放假节日（含清明、除夕、春节、端午、中秋）。</summary>
        private static string GetLegalFestival(DateTime date)
        {
            // ---- 公历固定日期 ----
            if (date.Month == 1 && date.Day == 1)
            {
                return "元旦";
            }
            if (date.Month == 5 && date.Day == 1)
            {
                return "劳动节";
            }
            if (date.Month == 10 && date.Day == 1)
            {
                return "国庆节";
            }

            // ---- 清明节：二十四节气"清明"当天（仅 2001-2100 年可算）----
            if (date.Month == 4 && date.Year >= SolarTermMinYear && date.Year <= SolarTermMaxYear
                && GetSolarTermDay(date.Year, TermQingMing) == date.Day)
            {
                return "清明节";
            }

            // ---- 农历固定日期 ----
            LunarDateInfo lunar = LunarCalendarService.Get(date);
            if (lunar != null && !lunar.IsLeapMonth)
            {
                if (lunar.LunarMonth == 1 && lunar.LunarDay == 1)
                {
                    return "春节";
                }
                if (lunar.LunarMonth == 5 && lunar.LunarDay == 5)
                {
                    return "端午节";
                }
                if (lunar.LunarMonth == 8 && lunar.LunarDay == 15)
                {
                    return "中秋节";
                }
            }

            // ---- 除夕：次日的农历为（非闰）正月初一 ----
            // 这种写法天然跨公历年正确：无论是腊月廿九还是腊月三十，
            // 也不管春节落在 1 月还是 2 月，都只需要看"明天是不是正月初一"。
            LunarDateInfo tomorrow = LunarCalendarService.Get(date.Date.AddDays(1));
            if (tomorrow != null && !tomorrow.IsLeapMonth
                && tomorrow.LunarMonth == 1 && tomorrow.LunarDay == 1)
            {
                return "除夕";
            }

            return null;
        }

        /// <summary>传统节日（按本类口径不含已归入法定节日的那几个）。</summary>
        private static string GetTraditionalFestival(DateTime date)
        {
            LunarDateInfo lunar = LunarCalendarService.Get(date);
            if (lunar == null || lunar.IsLeapMonth)
            {
                // 闰月不过节：闰五月初五不是端午节
                return null;
            }

            if (lunar.LunarMonth == 1 && lunar.LunarDay == 15)
            {
                return "元宵节";
            }
            if (lunar.LunarMonth == 2 && lunar.LunarDay == 2)
            {
                return "龙抬头";
            }
            if (lunar.LunarMonth == 7 && lunar.LunarDay == 7)
            {
                return "七夕节";
            }
            if (lunar.LunarMonth == 7 && lunar.LunarDay == 15)
            {
                return "中元节";
            }
            if (lunar.LunarMonth == 9 && lunar.LunarDay == 9)
            {
                return "重阳节";
            }
            if (lunar.LunarMonth == 12 && lunar.LunarDay == 8)
            {
                return "腊八节";
            }

            // 注意：小年（腊月廿三/廿四）各地风俗不同，为避免给出有争议的日期，这里不判断。
            return null;
        }

        /// <summary>查例外修正表；没有例外返回 0。</summary>
        private static int GetTermExceptionDelta(int year, int termIndex)
        {
            for (int i = 0; i < TermExceptions.Length; i++)
            {
                if (TermExceptions[i].Year == year && TermExceptions[i].TermIndex == termIndex)
                {
                    return TermExceptions[i].Delta;
                }
            }
            return 0;
        }
    }
}
