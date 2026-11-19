namespace TlsClient.Core.Models.Requests
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsCloseInput)
    public class WsCloseRequest
    {
        public string ConnectionId { get; set; } = string.Empty;
    }
}
