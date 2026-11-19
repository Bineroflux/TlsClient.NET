using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TlsClient.Core.Converters;
using TlsClient.Core.Models.Entities;

namespace TlsClient.Core.Models.Requests
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsConnectInput)
    /// <summary>
    /// Opens a WebSocket connection. The handshake is HTTP/1.1 over the same uTLS ClientHello the
    /// client profile uses for requests (ALPN offers <c>http/1.1</c> only). Properties left unset are
    /// taken from the client's <see cref="TlsClientOptions"/>, like a <see cref="Request"/>'s.
    /// </summary>
    public class WsConnectRequest
    {
        /// <summary><c>ws://</c> or <c>wss://</c> URL. May name an IP address: see <see cref="Headers"/> for
        /// the Host header and <see cref="ServerNameOverwrite"/> for the TLS server name.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Reuses an existing session's TLS client as it is. Never taken from the options: the session
        /// must have been created with <see cref="Request.ForceHttp1"/>, otherwise the handshake fails when
        /// the server negotiates HTTP/2. When unset, the native side builds a client for this connection
        /// alone (no cookie jar, no redirects).
        /// </summary>
        public Guid? SessionId { get; set; }

        [JsonConverter(typeof(JsonStringConverter<TlsClientIdentifier>))]
        public TlsClientIdentifier? TlsClientIdentifier { get; set; } = null;
        public CustomTlsClient? CustomTlsClient { get; set; }
        public string? ProxyUrl { get; set; }
        public string? ServerNameOverwrite { get; set; }

        /// <summary>
        /// Request headers, sent with their names spelled as given. A <c>Host</c> entry (exactly that
        /// spelling) sets the Host header. The handshake writes <c>Upgrade</c>, <c>Connection</c> and
        /// <c>Sec-WebSocket-Key</c> / <c>-Version</c> / <c>-Extensions</c> itself: passing one in Go's
        /// canonical spelling (<c>Sec-Websocket-Key</c>) fails the connect, any other spelling sends it
        /// twice. <c>Sec-WebSocket-Protocol</c> may be passed and is sent as given.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        /// <summary>Header order on the wire (lower-cased by the native side); headers it doesn't list follow.</summary>
        public List<string>? HeaderOrder { get; set; }

        /// <summary>Bounds the whole handshake (dial, TLS, HTTP); 0 = gorilla's default.</summary>
        public int HandshakeTimeoutMilliseconds { get; set; }
        public int ReadBufferSize { get; set; }
        public int WriteBufferSize { get; set; }
        public bool? InsecureSkipVerify { get; set; }
        public bool? WithRandomTLSExtensionOrder { get; set; }
        public bool? DisableIPV6 { get; set; }
        public bool? DisableIPV4 { get; set; }
    }
}
