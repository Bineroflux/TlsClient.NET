namespace TlsClient.Core.Models.Requests
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsReadInput)
    public class WsReadRequest
    {
        public string ConnectionId { get; set; } = string.Empty;

        /// <summary>
        /// How long to wait for the next message. <c>0</c> waits until one arrives (or the connection
        /// ends), blocking the calling thread for that long; <c>&gt; 0</c> returns a timeout after that
        /// many milliseconds, and the connection stays usable.
        /// </summary>
        public int TimeoutMilliseconds { get; set; }
    }
}
