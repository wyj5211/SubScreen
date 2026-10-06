using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SubScreen.Net
{
    /// <summary>
    /// 极薄的 HTTP GET 封装。全进程共用一个 HttpClient，避免 socket 耗尽。
    /// </summary>
    public static class HttpService
    {
        private static readonly object Sync = new object();
        private static HttpClient _client;

        /// <summary>
        /// 懒创建 + 可重试。
        ///
        /// 原来写成 static readonly 字段初始化，一旦构造失败（例如某些受限环境里
        /// 创建 HttpClientHandler 被拒），类型初始值设定项就会永久失败，
        /// 之后每次访问都抛 TypeInitializationException，再也恢复不了。
        /// 改成属性后，失败只影响当次调用，下一次还可以重试。
        /// </summary>
        private static HttpClient Client
        {
            get
            {
                HttpClient existing = _client;
                if (existing != null)
                {
                    return existing;
                }

                lock (Sync)
                {
                    if (_client == null)
                    {
                        _client = CreateClient();
                    }
                    return _client;
                }
            }
        }

        private static HttpClient CreateClient()
        {
            try
            {
                // .NET Framework 4.7.2 已经默认走系统设置，这里显式补上 TLS 1.2，兼容较老的配置
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception)
            {
            }

            HttpClientHandler handler;
            try
            {
                handler = new HttpClientHandler();
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                handler.AllowAutoRedirect = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "无法创建 HTTP 处理器（可能是运行环境限制了网络组件）：" + ex.Message, ex);
            }

            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);
            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SubScreen/1.0");
            }
            catch (Exception)
            {
            }

            return client;
        }

        public static async Task<string> GetStringAsync(
            string url, IDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> kv in headers)
                    {
                        request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }

                using (HttpResponseMessage response = await Client
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(string.Format(
                            "HTTP {0} {1}", (int)response.StatusCode, response.ReasonPhrase));
                    }

                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
