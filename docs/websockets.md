# 🔌 WebSockets

`TlsClient.Native` can open WebSocket connections whose handshake goes out with
the same TLS fingerprint as the client profile's requests. The native side is
tls-client's own WebSocket support (`cffi_src/websocket.go`): an HTTP/1.1
handshake over the profile's uTLS ClientHello (ALPN offers `http/1.1` only),
then a gorilla/websocket connection.

This is **native-only**, like [streaming](./streaming.md).

---

## 🧱 The WebSocket API

Five short, synchronous calls on `NativeTlsClient`, each mapping 1:1 to a native
entry point:

| `NativeTlsClient` method                                    | Native export | What it does |
| ----------------------------------------------------------- | ------------- | ------------ |
| `WsConnect(WsConnectRequest)`                               | `wsConnect`   | Runs the handshake; returns a `ConnectionId` (status 101) or the error. |
| `WsRead(string connectionId, int timeoutMs)`                | `wsRead`      | Returns the next message, or `Timeout` / `Error`. |
| `WsWrite(WsWriteRequest)` / `WsWriteText` / `WsWriteBinary` | `wsWrite`     | Sends one message. |
| `WsClose(string connectionId)`                              | `wsClose`     | Sends a close frame and releases the connection. Safe to call twice. |
| `WsStats(string connectionId)`                              | `wsStats`     | Diagnostic counters of a live connection; never touches the wire. |

`async` variants (`WsConnectAsync`, `WsReadAsync`, `WsWriteAsync`,
`WsCloseAsync`, `WsStatsAsync`) follow the same contract.

`NativeTlsClient.IsWebSocketSupported` reports whether the loaded native library
exposes the five entry points. Without them the methods return the error in
`Body`.

---

## 🚀 Quick Start

```csharp
using TlsClient.Native;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;

NativeTlsClient.Initialize("{PATH_TO_NATIVE_LIBRARY}");

using var client = new NativeTlsClient(new TlsClientOptions(
    TlsClientIdentifier.Chrome150,
    "Mozilla/5.0 ... Chrome/150"));

var connect = client.WsConnect(new WsConnectRequest
{
    Url = "wss://example.com/socket",
    Headers = { ["Origin"] = "https://example.com" },
    HandshakeTimeoutMilliseconds = 10_000,
});
if (!connect.IsConnected)
    throw new Exception(connect.Error);

var id = connect.ConnectionId;
try
{
    client.WsWriteText(id, "hello", timeoutMs: 5000);

    while (true)
    {
        var message = client.WsRead(id, timeoutMs: 1000);
        if (message.Timeout) continue;          // nothing yet; check your cancellation here
        if (message.IsTerminal) break;          // the connection is gone: message.Error says why
        Console.WriteLine(message.MessageType == WsMessageType.Text
            ? message.Data
            : $"{message.GetDataBytes().Length} binary bytes");
    }
}
finally
{
    client.WsClose(id);
}
```

Unset properties of `WsConnectRequest` (identifier, proxy, server name,
`InsecureSkipVerify`, random extension order, IP family, header order) and
missing headers are taken from the client's `TlsClientOptions`, like a
`Request`'s. The session is not: see below.

---

## 📨 Headers

* Headers go out with their names **spelled as given** (Go writes HTTP/1.1
  header names verbatim), so `Sec-WebSocket-Protocol` and
  `sec-websocket-protocol` differ on the wire.
* A `Host` entry, in exactly that spelling, sets the Host header. With it, `Url`
  may name an IP address while `Host` and `ServerNameOverwrite` keep the name —
  handy when the name resolves somewhere you don't want to dial.
* The handshake writes `Upgrade`, `Connection`, `Sec-WebSocket-Key`,
  `Sec-WebSocket-Version` and `Sec-WebSocket-Extensions` itself. Passing one in
  Go's canonical spelling (`Sec-Websocket-Key`) fails the connect; any other
  spelling sends it twice. Don't pass them.
* Subprotocols are offered by passing `Sec-WebSocket-Protocol` as a header.
* `permessage-deflate` is always offered, as
  `server_no_context_takeover; client_no_context_takeover`.

---

## 🔁 Reading

The native side reads the connection in its own goroutine, so ping/pong and
close frames are answered while nobody is in `WsRead`. Messages wait there, one
at a time, until a `WsRead` collects them.

| `timeoutMs` | Behaviour |
| ----------- | --------- |
| `0`         | Blocks the calling thread until a message arrives or the connection ends. |
| `> 0`       | Blocks up to that long, then returns `Timeout = true`; the connection stays usable. |

A text message arrives as-is in `Data`; a binary one base64-encoded
(`GetDataBytes()` decodes either). Any other end is terminal (`IsTerminal`,
`Error` set) and the id is invalid afterwards. A close from the server reads
like `failed to read message: websocket: close 4001 (…): reason` — the code and
reason are in the text.

---

## 🧩 Single-thread tunneling

`WsConnect` blocks for the whole handshake and `WsRead(…, 0)` until a message
arrives, so on a single dispatcher thread for every native call, poll with a
short `timeoutMs` (1 ms is the shortest) and wait between polls off that thread.
`WsWrite` blocks until the kernel takes the bytes, which is immediate unless the
peer stopped reading; bound it with `timeoutMs`.

---

## 🚧 Limitations

* **No handshake response.** A refused handshake reads `websocket: bad handshake`
  without its status or headers, and the subprotocol a server selected isn't
  reported.
* **No close code outbound.** `WsClose` always sends 1000.
* **Sessions.** `WsConnectRequest.SessionId` reuses that session's TLS client as
  it is. It must have been created with `ForceHttp1`; a session made by
  `Request()` negotiates HTTP/2 and the handshake fails. It is therefore never
  taken from the options: without it, each connection gets a client of its own
  (no cookie jar, no redirects).
* **Whole messages.** Reads and writes are message-level; fragments are joined
  on the native side.
