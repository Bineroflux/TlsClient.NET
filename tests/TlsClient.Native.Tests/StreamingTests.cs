using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TlsClient.Core.Helpers;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
using TlsClient.Core.Models.Responses;
using TlsClient.Native;
using TlsClient.Native.Wrappers;
using Xunit;

namespace TlsClient.Core.Tests
{
    /// <summary>
    /// A local HTTP server for the streaming tests, so they need no network access.
    /// </summary>
    public sealed class LocalStreamingServer : IDisposable
    {
        // Multi-byte UTF-8 characters (ä, €, emoji) so a small block size splits them across chunks.
        public const string SseEvent = "data: grüße €uro 😀\n\n";
        public const int SseEventCount = 3;
        public const string JsonBody = "{\"hello\":\"wörld\"}";

        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public string BaseUrl { get; }

        public LocalStreamingServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            BaseUrl = $"http://localhost:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                _ = Task.Run(() => Handle(context));
            }
        }

        private static async Task Handle(HttpListenerContext context)
        {
            var response = context.Response;
            try
            {
                switch (context.Request.Url!.AbsolutePath)
                {
                    case "/sse":
                        response.ContentType = "text/event-stream; charset=utf-8";
                        response.SendChunked = true;
                        var eventBytes = Encoding.UTF8.GetBytes(SseEvent);
                        for (int i = 0; i < SseEventCount; i++)
                        {
                            await response.OutputStream.WriteAsync(eventBytes);
                            await response.OutputStream.FlushAsync();
                            await Task.Delay(20);
                        }
                        break;
                    case "/setcookie":
                        response.Headers.Add("Set-Cookie", "sid=abc; Path=/");
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("set"));
                        break;
                    case "/echo-cookie":
                        var cookie = context.Request.Headers["Cookie"] ?? "<none>";
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("cookie=" + cookie));
                        break;
                    default:
                        response.ContentType = "application/json; charset=utf-8";
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(JsonBody));
                        break;
                }
            }
            finally
            {
                response.Close();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
        }
    }

    public class StreamingTests : IClassFixture<LocalStreamingServer>
    {
        private readonly LocalStreamingServer _server;

        static StreamingTests()
        {
            NativeTlsClient.Initialize(NativeTestSetup.DllPath);
        }

        public StreamingTests(LocalStreamingServer server)
        {
            _server = server;
        }

        private static NativeTlsClient NewClient(bool withDebug = false) =>
            new NativeTlsClient(new TlsClientOptions(TlsClientIdentifier.Chrome150, "tls-client-tests")
            {
                Timeout = Timeout.InfiniteTimeSpan,
                WithDebug = withDebug,
            });

        [Fact]
        public void Native_Library_Exposes_Streaming()
        {
            Assert.True(NativeTlsClient.IsStreamingSupported,
                $"The library at {NativeTestSetup.EnvVar} must be built with the streaming exports.");
        }

        [Fact]
        public void ReadStream_Delivers_The_Exact_Bytes_Until_EOF()
        {
            using var client = NewClient();
            var start = client.RequestStream(new Request
            {
                RequestUrl = _server.BaseUrl + "/sse",
                StreamOutputBlockSize = 5, // splits the multi-byte characters across chunks
            });
            Assert.Equal(HttpStatusCode.OK, start.Status);
            Assert.NotEqual(Guid.Empty, start.StreamId);

            var bytes = new List<byte>();
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    var chunk = client.ReadStream(start.StreamId, timeoutMs: 1000);
                    Assert.True(string.IsNullOrEmpty(chunk.Error), chunk.Error);
                    if (chunk.Timeout) continue;
                    bytes.AddRange(chunk.GetChunkBytes());
                    if (chunk.EOF) break;
                }

                var expected = string.Concat(System.Linq.Enumerable.Repeat(LocalStreamingServer.SseEvent, LocalStreamingServer.SseEventCount));
                Assert.Equal(expected, Encoding.UTF8.GetString(bytes.ToArray()));

                var afterEof = client.ReadStream(start.StreamId, timeoutMs: 0);
                Assert.Contains("unknown streamId", afterEof.Error);
                Assert.True(afterEof.IsTerminal);
            }
            finally
            {
                Assert.True(client.CancelStream(start.StreamId).Success, "CancelStream must be idempotent after EOF");
            }
        }

        [Fact]
        public void ReadStreamAll_Returns_The_Whole_Body()
        {
            using var client = NewClient();
            var start = client.RequestStream(new Request { RequestUrl = _server.BaseUrl + "/json" });
            Assert.Equal(HttpStatusCode.OK, start.Status);

            var full = client.ReadStreamAll(start.StreamId);

            Assert.Equal(HttpStatusCode.OK, full.Status);
            Assert.Equal(LocalStreamingServer.JsonBody, full.Body);
        }

        [Fact]
        public void ReadStream_Reports_An_Unknown_Stream()
        {
            using var client = NewClient();
            var chunk = client.ReadStream(Guid.NewGuid(), timeoutMs: 0);

            Assert.Contains("unknown streamId", chunk.Error);
            Assert.True(chunk.IsTerminal);
        }

        [Fact]
        public void Request_With_TimeoutSeconds_Is_Accepted()
        {
            // Options.Timeout is non-zero; it must not be sent along with TimeoutSeconds,
            // which the native side would reject.
            using var client = new NativeTlsClient(new TlsClientOptions(TlsClientIdentifier.Chrome150, "tls-client-tests"));
            var start = client.RequestStream(new Request { RequestUrl = _server.BaseUrl + "/json", TimeoutSeconds = -1 });

            Assert.Equal(HttpStatusCode.OK, start.Status);
            Assert.Equal(LocalStreamingServer.JsonBody, client.ReadStreamAll(start.StreamId).Body);
        }

        [Fact]
        public void WithDebug_Session_Keeps_Its_Cookies()
        {
            using var client = NewClient(withDebug: true);

            client.Request(new Request { RequestUrl = _server.BaseUrl + "/setcookie" });
            var response = client.Request(new Request { RequestUrl = _server.BaseUrl + "/echo-cookie" });

            Assert.Equal("cookie=sid=abc", response.Body);
        }

        [Fact]
        public void Wrapper_Rejects_A_Span_Payload_Without_Nul_Terminator()
        {
            var json = Encoding.UTF8.GetBytes("{}");

            Assert.Throws<ArgumentException>(() => TlsClientWrapper.Request(new ReadOnlySpan<byte>(json)));
        }

        [Fact]
        public void Wrapper_Byte_Array_Overload_Still_Works_Without_Nul_Terminator()
        {
            var payload = RequestHelpers.Prepare(new Request
            {
                RequestUrl = _server.BaseUrl + "/json",
                TlsClientIdentifier = TlsClientIdentifier.Chrome150,
            });

            var raw = TlsClientWrapper.Request(payload);
            var response = raw.FromJson<Response>();
            TlsClientWrapper.FreeMemory(response.Id);

            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(LocalStreamingServer.JsonBody, response.Body);
        }

#pragma warning disable CS0618 // Firefox148Psk is obsolete on purpose
        [Fact]
        public void Obsolete_Firefox148Psk_Maps_To_Firefox148()
        {
            Assert.Same(TlsClientIdentifier.Firefox148, TlsClientIdentifier.Firefox148Psk);
        }
#pragma warning restore CS0618
    }
}
