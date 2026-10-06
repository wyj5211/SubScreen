using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SubScreen.Net
{
    /// <summary>
    /// 城市查询结果（由 GeoAPI /geo/v2/city/lookup 解析而来）。
    /// </summary>
    public sealed class CityLookupResult
    {
        /// <summary>和风 LocationID，如 101010100。</summary>
        public string LocationId;
        /// <summary>城市名，如“北京”。</summary>
        public string CityName;
        /// <summary>一级行政区（省/直辖市），如“北京市”。</summary>
        public string Adm1;
        /// <summary>二级行政区（市/区），如“北京”。</summary>
        public string Adm2;
    }

    /// <summary>
    /// 和风天气接口异常。Code 为和风返回的业务码（如 "401"、"404"）；
    /// 当错误发生在本地（未配置、网络异常、响应无法解析）时为 null。
    /// </summary>
    public sealed class QWeatherException : Exception
    {
        /// <summary>和风返回的 code；无法解析时为 null。</summary>
        public string Code { get; private set; }

        public QWeatherException(string message, string code)
            : this(message, code, null)
        {
        }

        public QWeatherException(string message, string code, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }
    }

    /// <summary>
    /// 和风天气新版（API Host + X-QW-Api-Key 请求头）客户端。
    /// 只实现城市查询、实况、3 天预报三个只读接口。
    /// 所有方法均不阻塞调用线程，异常通过 Task 抛出。
    /// </summary>
    public sealed class QWeatherService
    {
        // -------- 接口路径（和风天气 v7 / GeoAPI v2） --------
        private const string GeoCityLookupPath = "/geo/v2/city/lookup";
        private const string WeatherNowPath = "/v7/weather/now";
        private const string Weather3DPath = "/v7/weather/3d";

        // -------- 认证请求头（新版接口认证方式） --------
        private const string ApiKeyHeaderName = "X-QW-Api-Key";

        // 业务成功码
        private const string SuccessCode = "200";

        /// <summary>规范化后的 Host（不含协议前缀、不含结尾斜杠）。</summary>
        private readonly string _apiHost;
        /// <summary>API Key（已去除首尾空白）。</summary>
        private readonly string _apiKey;
        /// <summary>拼接用的基础地址，形如 https://xxx.re.qweatherapi.com 。</summary>
        private readonly string _baseUrl;

        // 城市解析缓存：同一实例同一入参只请求一次。
        private readonly object _cityCacheLock = new object();
        private readonly Dictionary<string, CityLookupResult> _cityCache =
            new Dictionary<string, CityLookupResult>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 构造服务实例。apiHost 允许 "https://xxx.re.qweatherapi.com"、
        /// "xxx.re.qweatherapi.com"、"https://xxx.re.qweatherapi.com/" 等写法，内部统一规范化。
        /// 注意：此处不抛异常，配置缺失在调用具体方法时抛 QWeatherException。
        /// </summary>
        public QWeatherService(string apiHost, string apiKey)
        {
            _apiHost = NormalizeHost(apiHost);
            _apiKey = Trim(apiKey);
            _baseUrl = _apiHost.Length == 0 ? string.Empty : "https://" + _apiHost;
        }

        /// <summary>规范化后的 Host（便于界面显示/排查）。</summary>
        public string ApiHost
        {
            get { return _apiHost; }
        }

        /// <summary>规范化后的 Key。调用方用它判断设置里的 Key 是否变过，变了就得重建服务。</summary>
        public string ApiKey
        {
            get { return _apiKey; }
        }

        /// <summary>
        /// 城市解析：入参既可能是城市名（“北京”“海淀区”“朝阳”），也可能是 9 位数字 LocationID。
        /// 纯数字（长度 6-12）时原样返回、不发请求；否则调 GeoAPI 取第一条结果。
        /// 结果在实例内缓存。
        /// </summary>
        public async Task<CityLookupResult> ResolveCityAsync(string cityQuery, CancellationToken ct)
        {
            EnsureConfigured();

            if (cityQuery == null)
            {
                throw new QWeatherException("城市参数不能为空", null);
            }

            string query = cityQuery.Trim();
            if (query.Length == 0)
            {
                throw new QWeatherException("城市参数不能为空", null);
            }

            // 纯数字视为 LocationID，直接使用，不调用接口、也不联网。
            if (IsLocationIdLike(query))
            {
                CityLookupResult idHit = new CityLookupResult();
                idHit.LocationId = query;
                idHit.CityName = query;
                idHit.Adm1 = string.Empty;
                idHit.Adm2 = string.Empty;
                lock (_cityCacheLock)
                {
                    _cityCache[query] = idHit;
                }
                return idHit;
            }

            // 命中缓存直接返回（不重复请求）。
            lock (_cityCacheLock)
            {
                CityLookupResult cached;
                if (_cityCache.TryGetValue(query, out cached))
                {
                    return cached;
                }
            }

            ct.ThrowIfCancellationRequested();

            string url = _baseUrl + GeoCityLookupPath
                         + "?location=" + Uri.EscapeDataString(query)
                         + "&number=1";

            string json;
            try
            {
                json = await HttpService.GetStringAsync(url, BuildHeaders(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消异常必须冒泡，不能被包装。
                throw;
            }
            catch (QWeatherException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new QWeatherException("城市查询请求失败：" + ex.Message, null, ex);
            }

            CityLookupResult result = ParseCityLookup(json, query);

            lock (_cityCacheLock)
            {
                _cityCache[query] = result;
            }
            return result;
        }

        /// <summary>
        /// 拉取 3 天预报（/v7/weather/3d）与实况（/v7/weather/now），两个请求并行发起。
        /// 任一请求失败、或响应 code 非 200，都会抛 QWeatherException。
        /// </summary>
        public async Task<WeatherData> GetWeatherAsync(string cityQuery, CancellationToken ct)
        {
            EnsureConfigured();
            ct.ThrowIfCancellationRequested();

            CityLookupResult city = await ResolveCityAsync(cityQuery, ct).ConfigureAwait(false);
            if (city == null || string.IsNullOrEmpty(city.LocationId))
            {
                throw new QWeatherException("未能解析城市：" + (cityQuery == null ? string.Empty : cityQuery), null);
            }

            string location = Uri.EscapeDataString(city.LocationId);
            string nowUrl = _baseUrl + WeatherNowPath + "?location=" + location;
            string dailyUrl = _baseUrl + Weather3DPath + "?location=" + location;

            // 两个请求并行发起。
            Task<WeatherNow> nowTask = FetchNowAsync(nowUrl, ct);
            Task<List<WeatherDaily>> dailyTask = FetchDailyAsync(dailyUrl, ct);

            try
            {
                await Task.WhenAll(nowTask, dailyTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (QWeatherException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new QWeatherException("天气数据请求失败：" + ex.Message, null, ex);
            }

            // 此时两个任务均已完成，await 不会阻塞。
            WeatherNow now = await nowTask.ConfigureAwait(false);
            List<WeatherDaily> daily = await dailyTask.ConfigureAwait(false);

            WeatherData data = new WeatherData();
            data.CityName = city.CityName;
            data.LocationId = city.LocationId;
            data.FetchedAt = DateTime.Now;
            data.Now = now;
            data.Daily = daily;
            return data;
        }

        // ==================== 内部：HTTP 抓取 ====================

        private async Task<WeatherNow> FetchNowAsync(string url, CancellationToken ct)
        {
            string json;
            try
            {
                json = await HttpService.GetStringAsync(url, BuildHeaders(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (QWeatherException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new QWeatherException("实况天气请求失败：" + ex.Message, null, ex);
            }
            return ParseNow(json);
        }

        private async Task<List<WeatherDaily>> FetchDailyAsync(string url, CancellationToken ct)
        {
            string json;
            try
            {
                json = await HttpService.GetStringAsync(url, BuildHeaders(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (QWeatherException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new QWeatherException("天气预报请求失败：" + ex.Message, null, ex);
            }
            return ParseDaily(json);
        }

        /// <summary>每次请求都新建表头字典，避免并发复用。</summary>
        private IDictionary<string, string> BuildHeaders()
        {
            Dictionary<string, string> headers =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers[ApiKeyHeaderName] = _apiKey;
            return headers;
        }

        // ==================== 内部：配置校验与规范化 ====================

        private void EnsureConfigured()
        {
            if (_apiHost.Length == 0 || _apiKey.Length == 0)
            {
                throw new QWeatherException("未配置和风天气 API Host 或 Key", null);
            }
        }

        /// <summary>
        /// 把用户填写的 Host 规范化：去掉协议前缀、去掉结尾斜杠、去掉误带的路径。
        /// </summary>
        private static string NormalizeHost(string apiHost)
        {
            if (apiHost == null)
            {
                return string.Empty;
            }

            string host = apiHost.Trim();
            if (host.Length == 0)
            {
                return string.Empty;
            }

            if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                host = host.Substring("https://".Length);
            }
            else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                host = host.Substring("http://".Length);
            }
            else if (host.StartsWith("//", StringComparison.Ordinal))
            {
                host = host.Substring(2);
            }

            host = host.Trim();
            int slash = host.IndexOf('/');
            if (slash >= 0)
            {
                host = host.Substring(0, slash);
            }
            return host.Trim();
        }

        /// <summary>入参是否形如 LocationID（长度 6-12 的纯数字）。</summary>
        private static bool IsLocationIdLike(string value)
        {
            if (value.Length < 6 || value.Length > 12)
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        // ==================== 内部：JSON 解析 ====================

        private static Dictionary<string, object> DeserializeRoot(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new QWeatherException("和风天气返回了空响应", null);
            }

            object parsed;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 8 * 1024 * 1024;
                parsed = serializer.DeserializeObject(json);
            }
            catch (Exception ex)
            {
                throw new QWeatherException("和风天气响应解析失败：" + ex.Message, null, ex);
            }

            Dictionary<string, object> root = parsed as Dictionary<string, object>;
            if (root == null)
            {
                throw new QWeatherException("和风天气响应格式异常：顶层不是 JSON 对象", null);
            }
            return root;
        }

        /// <summary>校验业务码，非 200 抛 QWeatherException。</summary>
        private static void EnsureSuccessCode(Dictionary<string, object> root, string what)
        {
            string code = GetString(root, "code");
            if (!string.Equals(code, SuccessCode, StringComparison.Ordinal))
            {
                throw new QWeatherException(DescribeCode(code, what), string.IsNullOrEmpty(code) ? null : code);
            }
        }

        private static CityLookupResult ParseCityLookup(string json, string query)
        {
            Dictionary<string, object> root = DeserializeRoot(json);
            EnsureSuccessCode(root, "城市查询");

            foreach (object item in AsItems(GetValue(root, "location")))
            {
                Dictionary<string, object> entry = item as Dictionary<string, object>;
                if (entry == null)
                {
                    continue;
                }

                string id = GetString(entry, "id");
                if (id.Length == 0)
                {
                    continue;
                }

                CityLookupResult result = new CityLookupResult();
                result.LocationId = id;
                result.Adm1 = GetString(entry, "adm1");
                result.Adm2 = GetString(entry, "adm2");

                // 优先用 name（如“北京”），为空时逐级退化。
                string name = GetString(entry, "name");
                if (name.Length == 0)
                {
                    name = result.Adm2.Length > 0 ? result.Adm2 : result.Adm1;
                }
                if (name.Length == 0)
                {
                    name = query;
                }
                result.CityName = name;
                return result; // number=1，只取第一条
            }

            throw new QWeatherException("未找到城市：" + query + "（GeoAPI 未返回结果）", null);
        }

        private static WeatherNow ParseNow(string json)
        {
            Dictionary<string, object> root = DeserializeRoot(json);
            EnsureSuccessCode(root, "实况天气");

            WeatherNow now = new WeatherNow();
            Dictionary<string, object> node = GetObject(root, "now");
            if (node == null)
            {
                // 容错：缺失 now 节点时返回默认值，不抛异常。
                return now;
            }

            now.ObsTime = GetString(node, "obsTime");
            now.Temp = ParseDouble(GetValue(node, "temp"), 0d);
            now.FeelsLike = ParseDouble(GetValue(node, "feelsLike"), 0d);
            now.Icon = ParseInt(GetValue(node, "icon"), 0);
            now.Text = GetString(node, "text");
            now.WindDir = GetString(node, "windDir");
            now.WindScale = GetString(node, "windScale");
            now.WindSpeed = ParseDouble(GetValue(node, "windSpeed"), 0d);
            now.Humidity = ParseInt(GetValue(node, "humidity"), 0);
            now.Precip = ParseDouble(GetValue(node, "precip"), 0d);
            now.Pressure = ParseDouble(GetValue(node, "pressure"), 0d);
            now.Vis = ParseDouble(GetValue(node, "vis"), 0d);
            now.Cloud = ParseInt(GetValue(node, "cloud"), 0);
            now.Dew = ParseDouble(GetValue(node, "dew"), 0d);
            return now;
        }

        private static List<WeatherDaily> ParseDaily(string json)
        {
            Dictionary<string, object> root = DeserializeRoot(json);
            EnsureSuccessCode(root, "天气预报");

            List<WeatherDaily> list = new List<WeatherDaily>();
            foreach (object item in AsItems(GetValue(root, "daily")))
            {
                Dictionary<string, object> node = item as Dictionary<string, object>;
                if (node == null)
                {
                    continue;
                }

                WeatherDaily day = new WeatherDaily();
                day.Date = ParseDate(GetString(node, "fxDate"));
                day.Sunrise = GetString(node, "sunrise");
                day.Sunset = GetString(node, "sunset");
                day.MoonPhase = GetString(node, "moonPhase");
                day.TempMax = ParseDouble(GetValue(node, "tempMax"), 0d);
                day.TempMin = ParseDouble(GetValue(node, "tempMin"), 0d);
                day.IconDay = ParseInt(GetValue(node, "iconDay"), 0);
                day.TextDay = GetString(node, "textDay");
                day.IconNight = ParseInt(GetValue(node, "iconNight"), 0);
                day.TextNight = GetString(node, "textNight");
                day.WindDirDay = GetString(node, "windDirDay");
                day.WindScaleDay = GetString(node, "windScaleDay");
                day.WindDirNight = GetString(node, "windDirNight");
                day.WindScaleNight = GetString(node, "windScaleNight");
                day.Humidity = ParseInt(GetValue(node, "humidity"), 0);
                day.Precip = ParseDouble(GetValue(node, "precip"), 0d);
                day.Pressure = ParseDouble(GetValue(node, "pressure"), 0d);
                day.Vis = ParseDouble(GetValue(node, "vis"), 0d);
                day.UvIndex = ParseDouble(GetValue(node, "uvIndex"), 0d);
                list.Add(day);
            }
            return list;
        }

        // ==================== 内部：取值与容错转换 ====================

        private static object GetValue(Dictionary<string, object> dict, string key)
        {
            if (dict == null)
            {
                return null;
            }
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
        {
            return GetValue(dict, key) as Dictionary<string, object>;
        }

        /// <summary>取字符串；null、非字符串、空值都退化为空串（不抛异常）。</summary>
        private static string GetString(Dictionary<string, object> dict, string key)
        {
            object value = GetValue(dict, key);
            if (value == null)
            {
                return string.Empty;
            }

            string text = value as string;
            if (text != null)
            {
                return text.Trim();
            }

            IConvertible convertible = value as IConvertible;
            if (convertible != null)
            {
                try
                {
                    return convertible.ToString(CultureInfo.InvariantCulture).Trim();
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }

            string fallback = value.ToString();
            return fallback == null ? string.Empty : fallback.Trim();
        }

        /// <summary>
        /// 健壮的双精度解析：null、""、"  "、"N/A"、"abc" 等一律返回 fallback，不抛异常。
        /// </summary>
        private static double ParseDouble(object value, double fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            if (value is double)
            {
                return (double)value;
            }
            if (value is decimal)
            {
                return (double)(decimal)value;
            }
            if (value is float)
            {
                return (float)value;
            }

            string text = value as string;
            if (text == null)
            {
                IConvertible convertible = value as IConvertible;
                if (convertible == null)
                {
                    return fallback;
                }
                try
                {
                    return convertible.ToDouble(CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    return fallback;
                }
            }

            text = text.Trim();
            if (text.Length == 0)
            {
                return fallback;
            }

            double parsed;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        /// <summary>
        /// 健壮的整数解析：null、""、"N/A"、"abc" 等一律返回 fallback，不抛异常。
        /// </summary>
        private static int ParseInt(object value, int fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            if (value is int)
            {
                return (int)value;
            }
            if (value is long)
            {
                return (int)(long)value;
            }

            string text = value as string;
            if (text != null)
            {
                text = text.Trim();
                if (text.Length == 0)
                {
                    return fallback;
                }

                int parsedInt;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    return parsedInt;
                }
            }

            // 退化为按 double 解析后取整（例如 "3.0"、"N/A"）。
            double parsedDouble = ParseDouble(value, double.NaN);
            if (double.IsNaN(parsedDouble) || double.IsInfinity(parsedDouble))
            {
                return fallback;
            }
            return (int)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);
        }

        /// <summary>解析 yyyy-MM-dd；失败时尝试通用解析，再失败返回 DateTime.MinValue。</summary>
        private static DateTime ParseDate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return DateTime.MinValue;
            }

            DateTime date;
            if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date))
            {
                return date;
            }
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return date;
            }
            return DateTime.MinValue;
        }

        /// <summary>
        /// 把 JSON 数组统一枚举为 object 序列。
        /// JavaScriptSerializer 的数组可能是 object[]，也可能是 ArrayList，两种都兼容。
        /// </summary>
        private static IEnumerable<object> AsItems(object value)
        {
            if (value == null)
            {
                yield break;
            }

            object[] array = value as object[];
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    yield return array[i];
                }
                yield break;
            }

            if (value is string || value is IDictionary)
            {
                yield break;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
            {
                yield break;
            }

            foreach (object item in enumerable)
            {
                yield return item;
            }
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        /// <summary>把和风业务码翻译成可直接显示在界面上的中文提示。</summary>
        private static string DescribeCode(string code, string what)
        {
            string hint;
            switch (code)
            {
                case "400":
                    hint = "请求参数错误";
                    break;
                case "401":
                    hint = "认证失败，请检查 API Key（新版接口需用 API Host + X-QW-Api-Key 请求头）";
                    break;
                case "402":
                    hint = "超过每日访问次数或余额不足";
                    break;
                case "403":
                    hint = "无权限访问该数据，或访问额度已用尽";
                    break;
                case "404":
                    hint = "查询的城市或数据不存在";
                    break;
                case "429":
                    hint = "请求过于频繁，请稍后再试";
                    break;
                case "500":
                    hint = "和风天气服务端错误";
                    break;
                default:
                    hint = "和风天气返回错误";
                    break;
            }

            if (string.IsNullOrEmpty(code))
            {
                return what + "失败：" + hint + "（响应中缺少 code 字段）";
            }
            return what + "失败：" + hint + "（code=" + code + "）";
        }
    }
}
