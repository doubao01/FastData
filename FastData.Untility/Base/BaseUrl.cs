using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace FastUntility.Base
{
    /// <summary>
    /// post、get、put到url
    /// </summary>
    public static class BaseUrl
    {
        private static readonly Lazy<HttpClient> conn;

        static BaseUrl()
        {
            conn = new Lazy<HttpClient>(CreateHttpClient);
        }

        /// <summary>
        /// 创建单例 HttpClient（只执行一次）
        /// 说明：HttpClient 是线程安全且设计为复用的，但不允许在每次请求时修改它的
        /// 实例级状态（DefaultRequestHeaders / BaseAddress / Timeout）。
        /// 因此所有固定配置都在这里一次性设置，http 属性 getter 只负责返回实例，
        /// 不再产生任何副作用（issue #13）。
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip });

            // 连接保持：仅在单例创建时设置一次，避免每次访问 http 重复 Add 造成
            // "Connection: keep-alive, keep-alive, ..." 的头部膨胀。
            client.DefaultRequestHeaders.Connection.Add("keep-alive");

            return client;
        }

        /// <summary>
        /// 共享 HttpClient 实例（无副作用）
        /// </summary>
        internal static HttpClient http
        {
            get { return conn.Value; }
        }


        #region get url(select)
        /// <summary>
        /// get url(select) - Async version
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="version">主版本号</param>
        /// <param name="minor">次版本号</param>
        /// <returns>响应内容</returns>
        public static async Task<string> GetUrlAsync(string url, int version = 1, int minor = 1)
        {
            try
            {
                var handle = new HttpRequestMessage();
                handle.Version = new Version(version, minor);
                handle.Content = new StringContent("", Encoding.UTF8);
                handle.Method = HttpMethod.Get;
                handle.RequestUri = new Uri(url);
                var response = await http.SendAsync(handle).ConfigureAwait(false);
                handle.Content?.Dispose();
                handle.Dispose();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BaseLog.SaveLog(url + ":" + ex.ToString(), "GetUrl_exp");
                return null;
            }
        }

        /// <summary>
        /// get url(select)
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="version">主版本号</param>
        /// <param name="minor">次版本号</param>
        /// <returns>响应内容</returns>
        public static string GetUrl(string url, int version = 1, int minor = 1)
        {
            return GetUrlAsync(url, version, minor).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        #endregion

        #region post url(insert)
        /// <summary>
        /// post url(insert) - Async version
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="dic">参数字典</param>
        /// <param name="version">主版本号</param>
        /// <param name="minor">次版本号</param>
        /// <param name="mediaType">媒体类型</param>
        /// <returns>响应内容</returns>
        public static async Task<string> PostUrlAsync(string url, Dictionary<string, object> dic, int version = 1, int minor = 1, string mediaType = "application/json")
        {
            var count = 0;
            foreach (var item in dic)
            {
                if (url.Contains("?"))
                    url = string.Format("{0}&{1}={2}", url, item.Key, item.Value);
                else
                {
                    if (count == 0)
                        url = string.Format("{0}?{1}={2}", url, item.Key, item.Value);
                    else
                        url = string.Format("{0}&{1}={2}", url, item.Key, item.Value);
                }
                count++;
            }

            var handle = new HttpRequestMessage();
            handle.Version = new Version(version, minor);
            handle.Content = new StringContent("", Encoding.UTF8, mediaType);
            handle.Method = HttpMethod.Put;
            handle.RequestUri = new Uri(url);
            var response = await http.SendAsync(handle).ConfigureAwait(false);
            handle.Content?.Dispose();
            handle.Dispose();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// post url(insert)
        /// </summary>
        public static string PostUrl(string url, Dictionary<string, object> dic, int version = 1, int minor = 1, string mediaType = "application/json")
        {
            try
            {
                return PostUrlAsync(url, dic, version, minor, mediaType).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Task.Factory.StartNew(() => { BaseLog.SaveLog(url + ":" + ex.ToString(), "PostUrl_exp"); });
                return null;
            }
        }
        #endregion

        #region post content(insert)
        /// <summary>
        /// post content(insert) - Async version
        /// </summary>
        public static async Task<string> PostContentAsync(string url, string param, int version = 1, int minor = 1, string mediaType = "application/json")
        {
            var handle = new HttpRequestMessage();
            handle.Version = new Version(version, minor);
            handle.Content = new StringContent(param, Encoding.UTF8, mediaType);
            handle.Method = HttpMethod.Put;
            handle.RequestUri = new Uri(url);
            var response = await http.SendAsync(handle).ConfigureAwait(false);
            handle.Content.Dispose();
            handle.Dispose();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// post content(insert)
        /// </summary>
        public static string PostContent(string url, string param, int version = 1, int minor = 1, string mediaType = "application/json")
        {
            try
            {
                return PostContentAsync(url, param, version, minor, mediaType).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Task.Factory.StartNew(() => { BaseLog.SaveLog(url + ":" + ex.ToString(), "PostUrl_exp"); });
                return null;
            }
        }
        #endregion

        #region put url (update)
        /// <summary>
        /// put url(update) - Async version
        /// </summary>
        public static async Task<string> PutUrlAsync(string url, Dictionary<string, object> dic, int version = 1, int minor = 1, string mediaType = "application/json")
        {
            var count = 0;
            foreach (var item in dic)
            {
                if (url.Contains("?"))
                    url = string.Format("{0}&{1}={2}", url, item.Key, item.Value);
                else
                {
                    if (count == 0)
                        url = string.Format("{0}?{1}={2}", url, item.Key, item.Value);
                    else
                        url = string.Format("{0}&{1}={2}", url, item.Key, item.Value);
                }
                count++;
            }

            var handle = new HttpRequestMessage();
            handle.Version = new Version(version, minor);
            handle.Content = new StringContent("", Encoding.UTF8, mediaType);
            handle.Method = HttpMethod.Put;
            handle.RequestUri = new Uri(url);

            var response = await http.SendAsync(handle).ConfigureAwait(false);
            handle.Content?.Dispose();
            handle.Dispose();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// put url(update)
        /// </summary>
        public static string PutUrl(string url, Dictionary<string, object> dic, int version = 1, int minor = 1, string mediaType = "application/json")
        {
            try
            {
                return PutUrlAsync(url, dic, version, minor, mediaType).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Task.Factory.StartNew(() => { BaseLog.SaveLog(url + ":" + ex.ToString(), "PutUrl_exp"); });
                return null;
            }
        }
        #endregion
        
        #region post soap
        /// <summary>
        /// post soap - Async version
        /// </summary>
        public static async Task<string> PostSoapAsync(string url, string method, Dictionary<string, object> param, string Namespace = "http://tempuri.org/", int version = 1, int minor = 1)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xml.Append("<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">");
            xml.Append("<soap:Header />");
            xml.Append("<soap:Body>");
            xml.AppendFormat("<{0} xmlns=\"{1}\">", method, Namespace);

            foreach (KeyValuePair<string, object> item in param)
            {
                xml.AppendFormat("<{0}>{1}</{0}>", item.Key, item.Value.ToStr().Replace("<", "&lt;").Replace(">", "&gt;"));
            }

            xml.AppendFormat("</{0}>", method);
            xml.Append("</soap:Body>");
            xml.Append("</soap:Envelope>");

            var handle = new HttpRequestMessage();
            handle.Version = new Version(version, minor);
            handle.Content = new StringContent(xml.ToString(), Encoding.UTF8, "text/xml");
            handle.Method = HttpMethod.Post;
            handle.RequestUri = new Uri(url);
            var response = await http.SendAsync(handle).ConfigureAwait(false);
            handle.Content.Dispose();
            handle.Dispose();
            var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            result = result.Replace("soap:Envelope", "Envelope");
            result = result.Replace("soap:Body", "Body");
            result = result.Replace(" xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"", "");
            result = result.Replace(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"", "");
            result = result.Replace(" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"", "");
            result = result.Replace(" xmlns=\"http://openmas.chinamobile.com/pulgin\"", "");
            result = result.Replace(string.Format(" xmlns=\"{0}\"", Namespace), "");
            return BaseXml.GetXmlString(result, string.Format("Envelope/Body/{0}Response/{0}Result", method)).Replace("&lt;", "<").Replace("&gt;", ">");
        }

        /// <summary>
        /// post soap
        /// </summary>
        public static string PostSoap(string url, string method, Dictionary<string, object> param, string Namespace = "http://tempuri.org/", int version = 1, int minor = 1)
        {
            try
            {
                return PostSoapAsync(url, method, param, Namespace, version, minor).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Task.Factory.StartNew(() => { BaseLog.SaveLog(url + ":" + ex.ToString(), "PostSoap_exp"); });
                return null;
            }
        }
        #endregion

        #region delete url (delete)
        /// <summary>
        /// delete url (delete) - Async version
        /// </summary>
        public static async Task<string> DeleteUrlAsync(string url, int version = 1, int minor = 1, string mediaType = "application/json")
        {
            var handle = new HttpRequestMessage();
            handle.Version = new Version(version, minor);
            handle.Content = new StringContent("", Encoding.UTF8, mediaType);
            handle.Method = HttpMethod.Delete;
            handle.RequestUri = new Uri(url);
            var response = await http.SendAsync(handle).ConfigureAwait(false);
            handle.Content?.Dispose();
            handle.Dispose();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// delete url (delete)
        /// </summary>
        public static string DeleteUrl(string url, int version = 1, int minor = 1, string mediaType = "application/json")
        {
            try
            {
                return DeleteUrlAsync(url, version, minor, mediaType).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Task.Factory.StartNew(() => { BaseLog.SaveLog(url + ":" + ex.ToString(), "PutUrl_exp"); });
                BaseLog.SaveLog(url + ":" + ex.ToString(), "DeleteUrl_exp");
                return null;
            }
        }
        #endregion
    }
}
