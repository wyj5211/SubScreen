using System;
using System.Collections.Generic;

namespace SubScreen.Net
{
    /// <summary>
    /// 实况天气（对应和风天气 /v7/weather/now 的 now 节点）。
    /// </summary>
    public sealed class WeatherNow
    {
        /// <summary>观测时间原文（如 2021-02-16T12:53+08:00）。</summary>
        public string ObsTime;
        /// <summary>温度，摄氏度。</summary>
        public double Temp;
        /// <summary>体感温度，摄氏度。</summary>
        public double FeelsLike;
        /// <summary>和风图标代码。</summary>
        public int Icon;
        /// <summary>天气现象文字，如“晴”。</summary>
        public string Text;
        /// <summary>风向，如“西北风”。</summary>
        public string WindDir;
        /// <summary>风力等级，如“3”或“3-4”。</summary>
        public string WindScale;
        /// <summary>风速，km/h。</summary>
        public double WindSpeed;
        /// <summary>相对湿度，%。</summary>
        public int Humidity;
        /// <summary>降水量，mm。</summary>
        public double Precip;
        /// <summary>大气压强，hPa。</summary>
        public double Pressure;
        /// <summary>能见度，km。</summary>
        public double Vis;
        /// <summary>云量，%。</summary>
        public int Cloud;
        /// <summary>露点温度，摄氏度。</summary>
        public double Dew;
    }

    /// <summary>
    /// 单日预报（对应和风天气 /v7/weather/3d 的 daily 数组元素）。
    /// </summary>
    public sealed class WeatherDaily
    {
        /// <summary>预报日期。</summary>
        public DateTime Date;
        /// <summary>日出时间（HH:mm）。</summary>
        public string Sunrise;
        /// <summary>日落时间（HH:mm）。</summary>
        public string Sunset;
        /// <summary>月相名称。</summary>
        public string MoonPhase;
        /// <summary>最高温度，摄氏度。</summary>
        public double TempMax;
        /// <summary>最低温度，摄氏度。</summary>
        public double TempMin;
        /// <summary>白天天气图标代码。</summary>
        public int IconDay;
        /// <summary>白天天气现象文字。</summary>
        public string TextDay;
        /// <summary>夜间天气图标代码。</summary>
        public int IconNight;
        /// <summary>夜间天气现象文字。</summary>
        public string TextNight;
        /// <summary>白天风向。</summary>
        public string WindDirDay;
        /// <summary>白天风力等级。</summary>
        public string WindScaleDay;
        /// <summary>夜间风向。</summary>
        public string WindDirNight;
        /// <summary>夜间风力等级。</summary>
        public string WindScaleNight;
        /// <summary>相对湿度，%。</summary>
        public int Humidity;
        /// <summary>降水量，mm。</summary>
        public double Precip;
        /// <summary>大气压强，hPa。</summary>
        public double Pressure;
        /// <summary>能见度，km。</summary>
        public double Vis;
        /// <summary>紫外线指数。</summary>
        public double UvIndex;
    }

    /// <summary>
    /// 一次天气查询的聚合结果（实况 + 3 天预报）。
    /// </summary>
    public sealed class WeatherData
    {
        /// <summary>解析出的城市名（如“北京”）。</summary>
        public string CityName;
        /// <summary>和风 LocationID。</summary>
        public string LocationId;
        /// <summary>数据抓取时间（本机时间）。</summary>
        public DateTime FetchedAt;
        /// <summary>实况天气。</summary>
        public WeatherNow Now;
        /// <summary>逐日预报（3 天）。</summary>
        public List<WeatherDaily> Daily;
    }
}
