using System.Text.Json.Serialization;

namespace TlsClient.Core.Models.Responses
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsWriteOutput)
    /// <summary>Returned by <c>wsWrite</c>. A failed write closes the connection on the native side.</summary>
    public class WsWriteResponse : BaseResponse
    {
        public string ConnectionId { get; set; } = string.Empty;
        public bool Success { get; set; }

        [JsonIgnore]
        public string? Error => Success ? null : (string.IsNullOrEmpty(Body) ? "wsWrite failed without a message" : Body);
    }
}
