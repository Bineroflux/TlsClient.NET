using TlsClient.Core.Models.Entities;

namespace TlsClient.Core.Models.Requests
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsWriteInput)
    public class WsWriteRequest
    {
        public string ConnectionId { get; set; } = string.Empty;

        /// <summary>The message: text as-is, binary base64-encoded.</summary>
        public string Data { get; set; } = string.Empty;

        public WsMessageType MessageType { get; set; } = WsMessageType.Text;

        /// <summary>Bounds how long the write may block (peer not reading, dead path); a write that times
        /// out closes the connection. <c>0</c> = no deadline.</summary>
        public int TimeoutMilliseconds { get; set; }
    }
}
