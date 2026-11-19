using System;
using System.Text;
using System.Text.Json.Serialization;
using TlsClient.Core.Models.Entities;

namespace TlsClient.Core.Models.Responses
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsReadOutput)
    /// <summary>
    /// Returned by <c>wsRead</c>. Exactly one of these holds:
    /// <list type="bullet">
    /// <item><description>a message: <see cref="MessageType"/> and <see cref="Data"/> (see <see cref="GetDataBytes"/>);</description></item>
    /// <item><description><see cref="Timeout"/>: nothing arrived in time, and the connection is still open;</description></item>
    /// <item><description><see cref="Error"/>: the connection is gone and its id is invalid. A close from the
    /// server reads like <c>failed to read message: websocket: close 4001 (…): reason</c>, a read after
    /// <c>wsClose</c> like <c>websocket connection closed: …</c> or <c>no websocket connection found …</c>.</description></item>
    /// </list>
    /// </summary>
    public class WsReadResponse : BaseResponse
    {
        /// <summary>The error <c>wsRead</c> answers when nothing arrived within the timeout
        /// (<c>ErrWsReadTimeout</c> in the native source).</summary>
        public const string TimeoutMessage = "websocket read timeout";

        public string ConnectionId { get; set; } = string.Empty;
        public WsMessageType MessageType { get; set; }

        /// <summary>Text as-is, binary base64-encoded.</summary>
        public string? Data { get; set; }

        [JsonIgnore]
        public bool Timeout => Body == TimeoutMessage;

        [JsonIgnore]
        public string? Error => string.IsNullOrEmpty(Body) || Timeout ? null : Body;

        /// <summary>True when the connection has ended (<see cref="Error"/> is set). A <see cref="Timeout"/> is not terminal.</summary>
        [JsonIgnore]
        public bool IsTerminal => Error != null;

        /// <summary>The message's bytes: UTF-8 for a text message, decoded base64 for a binary one.</summary>
        public byte[] GetDataBytes()
        {
            if (string.IsNullOrEmpty(Data))
                return Array.Empty<byte>();
            return MessageType == WsMessageType.Binary ? Convert.FromBase64String(Data) : Encoding.UTF8.GetBytes(Data);
        }
    }
}
