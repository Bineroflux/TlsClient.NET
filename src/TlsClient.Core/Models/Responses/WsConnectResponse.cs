using System.Net;
using System.Text.Json.Serialization;

namespace TlsClient.Core.Models.Responses
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsConnectOutput)
    /// <summary>
    /// Returned by <c>wsConnect</c>. On success <see cref="Status"/> is 101 and
    /// <see cref="ConnectionId"/> names the connection for the other WebSocket calls. On failure
    /// <see cref="Status"/> is 0 and <see cref="BaseResponse.Body"/> holds the error. A handshake the
    /// server refused reads "websocket: bad handshake": the native side drops the response, so its
    /// status and headers aren't available, and neither is the subprotocol a server selected.
    /// </summary>
    public class WsConnectResponse : BaseResponse
    {
        public string ConnectionId { get; set; } = string.Empty;

        /// <summary>The session the connection's TLS client belongs to, when one was named.</summary>
        public string? SessionId { get; set; }

        public HttpStatusCode Status { get; set; }

        [JsonIgnore]
        public bool IsConnected => Status == HttpStatusCode.SwitchingProtocols && !string.IsNullOrEmpty(ConnectionId);

        [JsonIgnore]
        public string? Error => IsConnected ? null : (string.IsNullOrEmpty(Body) ? "wsConnect failed without a message" : Body);
    }
}
