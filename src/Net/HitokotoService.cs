using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SubScreen.Net
{
    internal sealed class Hitokoto
    {
        public string Text = string.Empty;
        public string From = string.Empty;
        public string FromWho = string.Empty;
        public string Uuid = string.Empty;
        public DateTime FetchedAt = DateTime.Now;

        /// <summary>格式化成"—— 出处"的附加说明；没有出处时返回空串。</summary>
        public string Attribution
        {
            get
            {
                if (string.IsNullOrEmpty(FromWho))
                {
                    return string.IsNullOrEmpty(From) ? string.Empty : "—— " + From;
                }

                if (string.IsNullOrEmpty(From))
                {
                    return "—— " + FromWho;
                }

                return "—— " + FromWho + "《" + From + "》";
            }
        }
    }

    /// <summary>一言（Hitokoto）客户端。</summary>
    internal static class HitokotoService
    {
        private const string Endpoint = "https://v1.hitokoto.cn/";

        public static async Task<Hitokoto> GetAsync(string categories, CancellationToken cancellationToken)
        {
            string url = Endpoint + "?encode=json&charset=utf-8";
            if (!string.IsNullOrEmpty(categories))
            {
                // 设置里允许写成 d&i&k 或 d,i,k
                // string normalized = categories.Replace(',', '&').Replace(' ', '&');
                // string[] parts = normalized.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
                if (categories.Length > 0)
                {
                    // url += "&c=" + Uri.EscapeDataString(string.Join("|", parts));
                    url += categories;
                }
            }

            string body = await HttpService
                .GetStringAsync(url, null, cancellationToken)
                .ConfigureAwait(false);

            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(body) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException("一言接口返回了无法解析的内容");
            }

            var result = new Hitokoto();
            result.Text = GetString(root, "hitokoto");
            result.From = GetString(root, "from");
            result.FromWho = GetString(root, "from_who");
            result.Uuid = GetString(root, "uuid");
            result.FetchedAt = DateTime.Now;

            if (string.IsNullOrEmpty(result.Text))
            {
                throw new InvalidOperationException("一言接口没有返回句子内容");
            }

            return result;
        }

        private static string GetString(Dictionary<string, object> bag, string key)
        {
            object v;
            if (bag.TryGetValue(key, out v) && v != null)
            {
                return Convert.ToString(v);
            }
            return string.Empty;
        }
    }
}
