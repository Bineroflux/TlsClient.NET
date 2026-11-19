using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TlsClient.Core;
using TlsClient.Core.Helpers;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
using TlsClient.Core.Models.Responses;
using TlsClient.Native.Wrappers;

namespace TlsClient.Native
{
    public sealed class NativeTlsClient : BaseTlsClient
    {
        public NativeTlsClient(TlsClientOptions options) : base(options) { }
        public NativeTlsClient() : base() { }

        public static void Initialize(string? libraryPath) => TlsClientWrapper.Initialize(libraryPath);

        #region Sync Methods
        public override Response Request(Request request)
        {
            request= PrepareRequest(request);

            Response response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(request);
                var rawResponse = TlsClientWrapper.Request(payload.TerminatedSpan);
                response = rawResponse.FromJson<Response>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new Response()
                {
                    Body = err.Message,
                    Status = 0,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            if (response.Status == 0 && response.Body.Contains("Client.Timeout exceeded"))
            {
                response = new Response()
                {
                    Body = "Timeout",
                    Status = HttpStatusCode.RequestTimeout,
                };
            }

            return response;
        }
        public override GetCookiesFromSessionResponse GetCookies(string url)
        {
            var input = PrepareGetCookies(url);
            using var payload = RequestHelpers.PreparePooled(input);
            var rawResponse = TlsClientWrapper.GetCookiesFromSession(payload.TerminatedSpan);
            
            return rawResponse.FromJson<GetCookiesFromSessionResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
        }
        public override GetCookiesFromSessionResponse AddCookies(string url, List<TlsClientCookie> cookies)
        {
            var input = PrepareAddCookies(url, cookies);
            using var payload = RequestHelpers.PreparePooled(input);
            var rawResponse = TlsClientWrapper.AddCookiesToSession(payload.TerminatedSpan);
            return rawResponse.FromJson<GetCookiesFromSessionResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
        }
        public override DestroyResponse Destroy()
        {
            var input = PrepareDestroy();
            using var payload = RequestHelpers.PreparePooled(input);
            var rawResponse = TlsClientWrapper.DestroySession(payload.TerminatedSpan);
            return rawResponse.FromJson<DestroyResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
        }
        public override DestroyResponse DestroyAll()
        {
            var rawResponse = TlsClientWrapper.DestroyAll();
            return rawResponse.FromJson<DestroyResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
        }
        #endregion

        #region Async Methods
        public override Task<Response> RequestAsync(Request request, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => Request(request), ct);
        public override Task<GetCookiesFromSessionResponse> GetCookiesAsync(string url, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => GetCookies(url), ct);
        public override Task<DestroyResponse> DestroyAsync(CancellationToken ct = default) => AsyncHelpers.RunAsync(() => Destroy(), ct);
        public override Task<DestroyResponse> DestroyAllAsync(CancellationToken ct = default) => AsyncHelpers.RunAsync(() => DestroyAll(), ct);
        public override Task<GetCookiesFromSessionResponse> AddCookiesAsync(string url, List<TlsClientCookie> cookies, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => AddCookies(url, cookies), ct);
        #endregion

        #region Streaming
        /// <summary>
        /// Returns <c>true</c> when the loaded native library exposes the streaming exports.
        /// Older builds may not — in which case the streaming methods on this client throw
        /// on first use.
        /// </summary>
        public static bool IsStreamingSupported => TlsClientWrapper.IsStreamingSupported;

        /// <summary>
        /// Issues a request and returns once the response headers are available, without
        /// reading the body. Use the returned <see cref="StreamStartResponse.StreamId"/>
        /// to drive subsequent <see cref="ReadStream"/> / <see cref="ReadStreamAll"/> /
        /// <see cref="CancelStream"/> calls.
        /// </summary>
        /// <remarks>
        /// For long-lived streaming responses (server-sent events, NDJSON, etc.)
        /// you must disable the native side's deadline by setting
        /// <see cref="Core.Models.Entities.TlsClientOptions.Timeout"/> to
        /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> on the client you
        /// stream with. <see cref="Core.Models.Entities.TlsClientOptions.Timeout"/>
        /// applies to the whole request including body reads, and the value <c>0</c>
        /// is interpreted as "use the 30 s default", not "no timeout". The timeout of a
        /// client's native session is fixed by its first request, so a negative
        /// <see cref="Request.TimeoutMilliseconds"/> on a later request has no effect;
        /// use a dedicated client for streaming.
        /// </remarks>
        public StreamStartResponse RequestStream(Request request)
        {
            request = PrepareRequest(request);

            StreamStartResponse response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(request);
                var rawResponse = TlsClientWrapper.RequestStream(payload.TerminatedSpan);
                response = rawResponse.FromJson<StreamStartResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new StreamStartResponse
                {
                    Body = err.Message,
                    Status = 0,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        /// <summary>
        /// Polls for the next chunk of an in-flight stream. Returns immediately on data,
        /// EOF, error, or after <paramref name="timeoutMs"/> when no chunk is available
        /// (heartbeat poll). Inspect <see cref="StreamChunkResponse.EOF"/>,
        /// <see cref="StreamChunkResponse.Timeout"/>, and <see cref="StreamChunkResponse.Error"/>
        /// to drive the read loop.
        /// </summary>
        /// <param name="timeoutMs">
        /// <c>&lt; 0</c>: block until the next chunk, EOF, or error.
        /// <c>= 0</c>: non-blocking poll.
        /// <c>&gt; 0</c>: block up to TimeoutMs, then return Timeout=true if no chunk arrived.
        /// </param>
        public StreamChunkResponse ReadStream(Guid streamId, int timeoutMs = 1000)
        {
            StreamChunkResponse response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(new ReadStreamRequest
                {
                    StreamId = streamId,
                    TimeoutMs = timeoutMs,
                });
                var rawResponse = TlsClientWrapper.ReadStream(payload.TerminatedSpan);
                response = rawResponse.FromJson<StreamChunkResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new StreamChunkResponse
                {
                    StreamId = streamId,
                    Error = err.Message,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        /// <summary>
        /// Drains the rest of the stream's body in one call and returns it as a normal
        /// <see cref="Response"/> (charset-decoded for text bodies, base64+MIME prefix for
        /// byte responses). Use this when the response from <see cref="RequestStream"/>
        /// turns out to NOT be a streaming response (e.g. Content-Type isn't
        /// <c>text/event-stream</c>) and you just want the full body in one shot.
        /// After this returns, <paramref name="streamId"/> is invalid.
        /// </summary>
        public Response ReadStreamAll(Guid streamId)
        {
            Response response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(new ReadStreamAllRequest { StreamId = streamId });
                var rawResponse = TlsClientWrapper.ReadStreamAll(payload.TerminatedSpan);
                response = rawResponse.FromJson<Response>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new Response
                {
                    Body = err.Message,
                    Status = 0,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        /// <summary>
        /// Cancels an in-flight stream and releases the underlying connection. Idempotent —
        /// safe to call after a natural EOF, after an error, or with an unknown
        /// <paramref name="streamId"/>. Always call this in a finally block when consuming
        /// chunks via <see cref="ReadStream"/>.
        /// </summary>
        public DestroyResponse CancelStream(Guid streamId)
        {
            DestroyResponse response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(new CancelStreamRequest { StreamId = streamId });
                var rawResponse = TlsClientWrapper.CancelStream(payload.TerminatedSpan);
                response = rawResponse.FromJson<DestroyResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new DestroyResponse
                {
                    Body = err.Message,
                    Success = false,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        public Task<StreamStartResponse> RequestStreamAsync(Request request, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => RequestStream(request), ct);
        public Task<StreamChunkResponse> ReadStreamAsync(Guid streamId, int timeoutMs = 1000, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => ReadStream(streamId, timeoutMs), ct);
        public Task<Response> ReadStreamAllAsync(Guid streamId, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => ReadStreamAll(streamId), ct);
        public Task<DestroyResponse> CancelStreamAsync(Guid streamId, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => CancelStream(streamId), ct);
        #endregion

        #region WebSockets
        /// <summary>
        /// Returns <c>true</c> when the loaded native library exposes the WebSocket exports.
        /// Older builds may not — in which case the WebSocket methods on this client return
        /// the error in <see cref="BaseResponse.Body"/>.
        /// </summary>
        public static bool IsWebSocketSupported => TlsClientWrapper.IsWebSocketSupported;

        /// <summary>
        /// Runs the WebSocket handshake and returns once it has completed or failed. Properties of
        /// <paramref name="request"/> left unset are taken from this client's options (not its session:
        /// see <see cref="WsConnectRequest.SessionId"/>). Use the returned
        /// <see cref="WsConnectResponse.ConnectionId"/> for <see cref="WsRead"/>, <see cref="WsWrite"/>,
        /// <see cref="WsClose"/> and <see cref="WsStats"/>.
        /// </summary>
        /// <remarks>
        /// The native side keeps reading the connection in the background, so ping/pong and close
        /// frames are answered while nobody is inside <see cref="WsRead"/>. Messages wait there, one at
        /// a time, until collected. Always call <see cref="WsClose"/> when done.
        /// </remarks>
        public WsConnectResponse WsConnect(WsConnectRequest request)
        {
            request = PrepareWsConnect(request);

            WsConnectResponse response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(request);
                var rawResponse = TlsClientWrapper.WsConnect(payload.TerminatedSpan);
                response = rawResponse.FromJson<WsConnectResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new WsConnectResponse
                {
                    Body = err.Message,
                    Status = 0,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        /// <summary>
        /// Collects the next message of a connection. Inspect <see cref="WsReadResponse.Timeout"/> and
        /// <see cref="WsReadResponse.Error"/> to drive the read loop.
        /// </summary>
        /// <param name="timeoutMs">
        /// <c>0</c>: block the calling thread until a message arrives or the connection ends.
        /// <c>&gt; 0</c>: block up to that many milliseconds, then return Timeout=true; the connection stays usable.
        /// </param>
        public WsReadResponse WsRead(string connectionId, int timeoutMs = 1000)
        {
            WsReadResponse response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(new WsReadRequest
                {
                    ConnectionId = connectionId,
                    TimeoutMilliseconds = timeoutMs,
                });
                var rawResponse = TlsClientWrapper.WsRead(payload.TerminatedSpan);
                response = rawResponse.FromJson<WsReadResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new WsReadResponse
                {
                    ConnectionId = connectionId,
                    Body = err.Message,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        /// <summary>
        /// Sends one message. Safe to call while another thread is inside <see cref="WsRead"/> on the
        /// same connection, and alongside other writes. A failed write closes the connection.
        /// </summary>
        public WsWriteResponse WsWrite(WsWriteRequest request)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            WsWriteResponse response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(request);
                var rawResponse = TlsClientWrapper.WsWrite(payload.TerminatedSpan);
                response = rawResponse.FromJson<WsWriteResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new WsWriteResponse
                {
                    ConnectionId = request.ConnectionId,
                    Body = err.Message,
                    Success = false,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        /// <summary>Sends a text message. See <see cref="WsWrite(WsWriteRequest)"/>.</summary>
        /// <param name="timeoutMs">Bounds how long the write may block; <c>0</c> = no deadline.</param>
        public WsWriteResponse WsWriteText(string connectionId, string text, int timeoutMs = 0)
            => WsWrite(new WsWriteRequest
            {
                ConnectionId = connectionId,
                Data = text,
                MessageType = WsMessageType.Text,
                TimeoutMilliseconds = timeoutMs,
            });

        /// <summary>Sends a binary message. See <see cref="WsWrite(WsWriteRequest)"/>.</summary>
        /// <param name="timeoutMs">Bounds how long the write may block; <c>0</c> = no deadline.</param>
        public WsWriteResponse WsWriteBinary(string connectionId, byte[] data, int timeoutMs = 0)
            => WsWrite(new WsWriteRequest
            {
                ConnectionId = connectionId,
                Data = Convert.ToBase64String(data ?? throw new ArgumentNullException(nameof(data))),
                MessageType = WsMessageType.Binary,
                TimeoutMilliseconds = timeoutMs,
            });

        /// <summary>
        /// Sends a close frame (always 1000; the native side takes no code) and releases the connection.
        /// A <see cref="WsRead"/> blocked on it returns with an error. Returns Success=false when the
        /// connection is already gone — after a read or write failure, or a close from the server — so
        /// it is safe to call in a finally block.
        /// </summary>
        public DestroyResponse WsClose(string connectionId)
        {
            DestroyResponse response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(new WsCloseRequest { ConnectionId = connectionId });
                var rawResponse = TlsClientWrapper.WsClose(payload.TerminatedSpan);
                response = rawResponse.FromJson<DestroyResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new DestroyResponse
                {
                    Body = err.Message,
                    Success = false,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        /// <summary>Returns a diagnostic snapshot of a live connection; never touches the wire.</summary>
        public WsStatsResponse WsStats(string connectionId)
        {
            WsStatsResponse response;

            try
            {
                using var payload = RequestHelpers.PreparePooled(new WsStatsRequest { ConnectionId = connectionId });
                var rawResponse = TlsClientWrapper.WsStats(payload.TerminatedSpan);
                response = rawResponse.FromJson<WsStatsResponse>() ?? throw new Exception("Response is null, can't convert object from json.");
            }
            catch (Exception err)
            {
                response = new WsStatsResponse
                {
                    ConnectionId = connectionId,
                    Body = err.Message,
                };
            }

            if (!string.IsNullOrEmpty(response.Id))
                TlsClientWrapper.FreeMemory(response.Id);

            return response;
        }

        public Task<WsConnectResponse> WsConnectAsync(WsConnectRequest request, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => WsConnect(request), ct);
        public Task<WsReadResponse> WsReadAsync(string connectionId, int timeoutMs = 1000, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => WsRead(connectionId, timeoutMs), ct);
        public Task<WsWriteResponse> WsWriteAsync(WsWriteRequest request, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => WsWrite(request), ct);
        public Task<DestroyResponse> WsCloseAsync(string connectionId, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => WsClose(connectionId), ct);
        public Task<WsStatsResponse> WsStatsAsync(string connectionId, CancellationToken ct = default) => AsyncHelpers.RunAsync(() => WsStats(connectionId), ct);
        #endregion
    }
}
