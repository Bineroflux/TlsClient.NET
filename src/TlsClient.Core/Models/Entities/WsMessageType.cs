namespace TlsClient.Core.Models.Entities
{
    /// <summary>
    /// The WebSocket message types tls-client reads and writes (gorilla/websocket's constants).
    /// Control frames (ping, pong, close) are handled on the native side and never surface here.
    /// </summary>
    public enum WsMessageType
    {
        Text = 1,
        Binary = 2,
    }
}
