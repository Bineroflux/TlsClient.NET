namespace TlsClient.Core.Models.Entities
{
    // Reference: https://github.com/bogdanfinn/tls-client/blob/master/cffi_src/types.go (WsTcpInfo)
    /// <summary>
    /// The kernel's view of a WebSocket connection's TCP socket (Linux <c>TCP_INFO</c>; absent on
    /// other platforms). With a proxy this is the connection to the proxy. A growing
    /// <see cref="Unacked"/> / <see cref="Retransmits"/> with a stale <see cref="MsSinceLastAckRecv"/>
    /// means the bytes aren't being acknowledged: the path is dead although writes still succeed.
    /// </summary>
    public class WsTcpInfo
    {
        /// <summary>1 = ESTABLISHED.</summary>
        public byte State { get; set; }
        public byte Retransmits { get; set; }
        public byte Backoff { get; set; }
        public uint RtoMs { get; set; }
        public uint RttMs { get; set; }
        public uint Unacked { get; set; }
        public uint Lost { get; set; }
        public uint TotalRetrans { get; set; }
        public uint NotsentBytes { get; set; }
        public uint MsSinceLastDataSent { get; set; }
        public uint MsSinceLastDataRecv { get; set; }
        public uint MsSinceLastAckRecv { get; set; }
    }
}
