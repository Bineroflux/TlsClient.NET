using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
using TlsClient.Native;
using Xunit;

namespace TlsClient.Core.Tests
{
    /// <summary>
    /// A local WebSocket server for the WebSocket tests, so they need no network access. On
    /// <c>/echo</c> it accepts the first offered subprotocol, sends one message describing the
    /// handshake, echoes every message, and closes with the code a <c>close:&lt;code&gt;:&lt;reason&gt;</c>
    /// text message names. <c>/reject</c> refuses the handshake with a 403.
    /// </summary>
    public sealed class LocalWebSocketServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public string BaseUrl { get; }

        public LocalWebSocketServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            BaseUrl = $"ws://localhost:{port}";
            _listener.Prefixes.Add($"http://localhost:{port}/");
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
            if (!context.Request.IsWebSocketRequest || context.Request.Url!.AbsolutePath == "/reject")
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                return;
            }

            var offered = context.Request.Headers["Sec-WebSocket-Protocol"]?.Split(',').Select(p => p.Trim()).ToArray() ?? Array.Empty<string>();
            var accepted = await context.AcceptWebSocketAsync(offered.FirstOrDefault());
            using var socket = accepted.WebSocket;

            var hello = $"protocol={socket.SubProtocol};offered={string.Join("|", offered)};x-test={context.Request.Headers["X-Test"]}";
            await socket.SendAsync(Encoding.UTF8.GetBytes(hello), WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    var message = new List<byte>();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                            return;
                        }
                        message.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    var bytes = message.ToArray();
                    if (result.MessageType == WebSocketMessageType.Text && Encoding.UTF8.GetString(bytes) is var text && text.StartsWith("close:"))
                    {
                        var parts = text.Split(':', 3);
                        await socket.CloseAsync((WebSocketCloseStatus)int.Parse(parts[1]), parts[2], CancellationToken.None);
                        return;
                    }

                    await socket.SendAsync(bytes, result.MessageType, true, CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // the client went away
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
        }
    }

    public class WebSocketTests : IClassFixture<LocalWebSocketServer>
    {
        private readonly LocalWebSocketServer _server;

        static WebSocketTests()
        {
            NativeTlsClient.Initialize(NativeTestSetup.DllPath);
        }

        public WebSocketTests(LocalWebSocketServer server)
        {
            _server = server;
        }

        private static NativeTlsClient NewClient() =>
            new NativeTlsClient(new TlsClientOptions(TlsClientIdentifier.Chrome150, "tls-client-tests"));

        private string Connect(NativeTlsClient client, string path = "/echo")
        {
            var connect = client.WsConnect(new WsConnectRequest
            {
                Url = _server.BaseUrl + path,
                Headers = new Dictionary<string, string>
                {
                    ["X-Test"] = "abc",
                    ["Sec-WebSocket-Protocol"] = "proto.v1, token123",
                },
                HandshakeTimeoutMilliseconds = 5000,
            });
            Assert.True(connect.IsConnected, connect.Error);
            Assert.Equal(HttpStatusCode.SwitchingProtocols, connect.Status);
            return connect.ConnectionId;
        }

        [Fact]
        public void Native_Library_Exposes_WebSockets()
        {
            Assert.True(NativeTlsClient.IsWebSocketSupported,
                $"The library at {NativeTestSetup.EnvVar} must be built with the WebSocket exports.");
        }

        [Fact]
        public void Handshake_Carries_The_Headers_And_Subprotocols()
        {
            using var client = NewClient();
            var id = Connect(client);
            try
            {
                var hello = client.WsRead(id, timeoutMs: 5000);

                Assert.Null(hello.Error);
                Assert.Equal(WsMessageType.Text, hello.MessageType);
                // The default User-Agent from the options is sent too, but the server doesn't report it.
                Assert.Equal("protocol=proto.v1;offered=proto.v1|token123;x-test=abc", hello.Data);
            }
            finally
            {
                client.WsClose(id);
            }
        }

        [Fact]
        public void Text_And_Binary_Messages_Round_Trip()
        {
            using var client = NewClient();
            var id = Connect(client);
            try
            {
                client.WsRead(id, timeoutMs: 5000); // the hello

                const string text = "grüße €uro 😀";
                Assert.True(client.WsWriteText(id, text, timeoutMs: 5000).Success);
                var textEcho = client.WsRead(id, timeoutMs: 5000);
                Assert.Equal(WsMessageType.Text, textEcho.MessageType);
                Assert.Equal(text, textEcho.Data);
                Assert.Equal(Encoding.UTF8.GetBytes(text), textEcho.GetDataBytes());

                var binary = new byte[] { 0, 1, 2, 255, 128 };
                Assert.True(client.WsWriteBinary(id, binary, timeoutMs: 5000).Success);
                var binaryEcho = client.WsRead(id, timeoutMs: 5000);
                Assert.Equal(WsMessageType.Binary, binaryEcho.MessageType);
                Assert.Equal(binary, binaryEcho.GetDataBytes());
            }
            finally
            {
                client.WsClose(id);
            }
        }

        [Fact]
        public void Read_Times_Out_And_The_Connection_Stays_Usable()
        {
            using var client = NewClient();
            var id = Connect(client);
            try
            {
                client.WsRead(id, timeoutMs: 5000); // the hello

                var idle = client.WsRead(id, timeoutMs: 50);
                Assert.True(idle.Timeout);
                Assert.Null(idle.Error);
                Assert.False(idle.IsTerminal);

                Assert.True(client.WsWriteText(id, "still there", timeoutMs: 5000).Success);
                Assert.Equal("still there", client.WsRead(id, timeoutMs: 5000).Data);
            }
            finally
            {
                client.WsClose(id);
            }
        }

        [Fact]
        public void A_Close_From_The_Server_Ends_The_Connection_With_Its_Code()
        {
            using var client = NewClient();
            var id = Connect(client);
            client.WsRead(id, timeoutMs: 5000); // the hello

            Assert.True(client.WsWriteText(id, "close:4001:bye now", timeoutMs: 5000).Success);
            var closed = client.WsRead(id, timeoutMs: 5000);

            Assert.True(closed.IsTerminal);
            Assert.Contains("close 4001", closed.Error);
            Assert.Contains("bye now", closed.Error);
            Assert.False(client.WsClose(id).Success, "the connection is gone after the server closed it");
        }

        [Fact]
        public void WsClose_Is_Idempotent_And_Ends_The_Connection()
        {
            using var client = NewClient();
            var id = Connect(client);

            Assert.True(client.WsClose(id).Success);
            Assert.False(client.WsClose(id).Success);
            Assert.True(client.WsRead(id, timeoutMs: 50).IsTerminal);
        }

        [Fact]
        public void A_Refused_Handshake_Reports_The_Error()
        {
            using var client = NewClient();
            var connect = client.WsConnect(new WsConnectRequest
            {
                Url = _server.BaseUrl + "/reject",
                HandshakeTimeoutMilliseconds = 5000,
            });

            Assert.False(connect.IsConnected);
            Assert.Equal((HttpStatusCode)0, connect.Status);
            Assert.Contains("bad handshake", connect.Error);
        }

        [Fact]
        public void WsStats_Counts_The_Messages_Of_A_Live_Connection()
        {
            using var client = NewClient();
            var id = Connect(client);
            try
            {
                client.WsRead(id, timeoutMs: 5000); // the hello
                Assert.True(client.WsWriteText(id, "one", timeoutMs: 5000).Success);
                client.WsRead(id, timeoutMs: 5000);

                var stats = client.WsStats(id);

                Assert.Null(stats.Error);
                Assert.Equal(id, stats.ConnectionId);
                Assert.Equal(2, stats.MessagesRead);
                Assert.Equal(1, stats.MessagesWritten);
                Assert.False(stats.UnreadPending);
            }
            finally
            {
                client.WsClose(id);
            }

            Assert.NotNull(client.WsStats(id).Error);
        }

        [Fact]
        public void WsConnect_Requires_A_Url()
        {
            using var client = NewClient();

            Assert.Throws<ArgumentException>(() => client.WsConnect(new WsConnectRequest()));
        }
    }
}
