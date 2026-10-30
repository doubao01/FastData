using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastData.Tests.DevTools
{
    /// <summary>
    /// 最小 HTTP 服务端，用于测试 <c>ApiClient</c>。
    /// <para>
    /// 为什么自己搭而不是用 <see cref="HttpListener"/>：后者在部分 CI 环境需要额外权限，
    /// 且行为随平台而异。直接用 <see cref="TcpListener"/> 手写响应，
    /// 端到端可控、不依赖任何平台特性，也不会因为环境差异导致测试时通时不通。
    /// </para>
    /// <para>
    /// 只支持 HTTP/1.1 的最小子集：够测试用，不追求完整实现。
    /// 每个实例监听一个随机空闲端口，用完即弃，测试之间互不干扰。
    /// </para>
    /// </summary>
    internal sealed class StubHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Thread _worker;
        private readonly Func<string, string> _responder;
        private volatile bool _stopped;

        /// <summary>
        /// 服务地址，形如 http://127.0.0.1:12345。
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// 记录收到的请求（原文首行 + 头 + 体），便于断言请求内容。
        /// </summary>
        public List<string> ReceivedRequests { get; } = new List<string>();

        /// <summary>
        /// </summary>
        /// <param name="responder">
        /// 收到一次请求时返回完整 HTTP 响应文本（含状态行与头）。
        /// 参数为请求原始内容。
        /// </param>
        public StubHttpServer(Func<string, string> responder)
        {
            _responder = responder;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUrl = string.Format("http://127.0.0.1:{0}", endpoint.Port);

            _worker = new Thread(Loop) { IsBackground = true };
            _worker.Start();
        }

        private void Loop()
        {
            while (!_stopped)
            {
                try
                {
                    using (var client = _listener.AcceptTcpClient())
                    using (var stream = client.GetStream())
                    {
                        var request = ReadRequest(stream);
                        if (request == null)
                            continue;

                        lock (ReceivedRequests)
                            ReceivedRequests.Add(request);

                        var responseText = _responder(request);
                        var bytes = Encoding.UTF8.GetBytes(responseText);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush();
                    }
                }
                catch (SocketException)
                {
                    // 监听器被 Dispose 时正常退出
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch
                {
                    // 单个连接异常不应让整个服务端停摆
                }
            }
        }

        /// <summary>
        /// 读到请求头结束（空行）为止，若有 Content-Length 再读对应长度的体。
        /// </summary>
        private static string ReadRequest(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            var headerEnd = -1;

            // 读头
            while (headerEnd < 0)
            {
                var n = stream.Read(buffer, 0, buffer.Length);
                if (n <= 0)
                    return null;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
                headerEnd = sb.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal);
            }

            var headerText = sb.ToString();
            var headerLength = headerEnd + 4;

            // 解析 Content-Length
            var contentLength = 0;
            foreach (var line in headerText.Substring(0, headerEnd).Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);
                }
            }

            // 读体
            var bodySoFar = headerText.Length - headerLength;
            while (bodySoFar < contentLength)
            {
                var n = stream.Read(buffer, 0, Math.Min(buffer.Length, contentLength - bodySoFar));
                if (n <= 0)
                    break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
                bodySoFar += n;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 构造一个正常的 JSON 响应。
        /// </summary>
        public static string Json(string json, int statusCode = 200, string reason = "OK")
        {
            var body = Encoding.UTF8.GetBytes(json);
            return string.Format(
                "HTTP/1.1 {0} {1}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {2}\r\nConnection: close\r\n\r\n{3}",
                statusCode, reason, body.Length, json);
        }

        /// <summary>
        /// 构造一个非 JSON 的纯文本响应。
        /// </summary>
        public static string Text(string text, int statusCode = 200, string reason = "OK")
        {
            var body = Encoding.UTF8.GetBytes(text);
            return string.Format(
                "HTTP/1.1 {0} {1}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {2}\r\nConnection: close\r\n\r\n{3}",
                statusCode, reason, body.Length, text);
        }

        public void Dispose()
        {
            _stopped = true;
            try { _listener.Stop(); } catch { }
            try { _listener.Server.Close(); } catch { }
        }
    }
}