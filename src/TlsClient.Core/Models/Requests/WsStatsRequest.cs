namespace TlsClient.Core.Models.Requests
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsStatsInput)
    public class WsStatsRequest
    {
        public string ConnectionId { get; set; } = string.Empty;
    }
}
