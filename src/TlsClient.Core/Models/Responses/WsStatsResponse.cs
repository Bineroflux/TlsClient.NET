using System.Text.Json.Serialization;
using TlsClient.Core.Models.Entities;

namespace TlsClient.Core.Models.Responses
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsStatsOutput)
    /// <summary>
    /// Returned by <c>wsStats</c>: a diagnostic snapshot of a live connection that never touches the
    /// wire. Counters are application messages, not control frames. <c>MsSince*</c> are -1 when nothing
    /// was read / written yet. A connection that already failed or was closed is gone: then
    /// <see cref="Error"/> is set.
    /// </summary>
    public class WsStatsResponse : BaseResponse
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string LocalAddr { get; set; } = string.Empty;
        public string RemoteAddr { get; set; } = string.Empty;
        public long AgeMs { get; set; }
        public long MessagesRead { get; set; }
        public long MessagesWritten { get; set; }
        public long BytesRead { get; set; }
        public long BytesWritten { get; set; }
        public long MsSinceLastRead { get; set; }
        public long MsSinceLastWrite { get; set; }
        public long LastWriteDurationMs { get; set; }
        public long MaxWriteDurationMs { get; set; }

        /// <summary>A message arrived that no <c>wsRead</c> has collected yet.</summary>
        public bool UnreadPending { get; set; }

        /// <summary>Linux only.</summary>
        public WsTcpInfo? Tcp { get; set; }

        [JsonIgnore]
        public string? Error => string.IsNullOrEmpty(Body) ? null : Body;
    }
}
