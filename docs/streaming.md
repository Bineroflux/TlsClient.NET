# 🔀 Streaming Responses (SSE / chunked)

`TlsClient.Native` can consume long-lived streaming responses — Server-Sent
Events (`text/event-stream`), NDJSON firehoses, or any chunked HTTP/1.1 / HTTP/2
body — without buffering the entire body in memory first.

This is **native-only**. `TlsClient.Api` doesn't expose it because the
upstream `tls-client` HTTP service doesn't forward chunked bodies in a
streaming fashion.

---

## 🧱 The streaming API

A streaming response is consumed through four short, synchronous calls on
`NativeTlsClient`. Each maps 1:1 to a native entry point in the underlying
`tls-client` library:

| `NativeTlsClient` method                          | Native export   | What it does                                                                |
| ------------------------------------------------- | --------------- | --------------------------------------------------------------------------- |
| `RequestStream(Request)`                          | `requestStream` | Issues the request and returns once **headers** are available; body stays open and is owned by the native side. |
| `ReadStream(Guid streamId, int timeoutMs)`        | `readStream`    | Returns the next chunk (base64-encoded bytes), or one of `EOF` / `Timeout` / `Error`. |
| `ReadStreamAll(Guid streamId)`                    | `readStreamAll` | Drains the rest of the body in one call. Returns the same `Response` shape as the non-streaming `Request()`. |
| `CancelStream(Guid streamId)`                     | `cancelStream`  | Closes the underlying connection and releases native resources. Idempotent — safe to call after EOF, error, or on an unknown id. |

`async` variants (`RequestStreamAsync`, `ReadStreamAsync`, `ReadStreamAllAsync`,
`CancelStreamAsync`) follow the same contract.

The static `NativeTlsClient.IsStreamingSupported` reports whether the loaded
native library exposes the four entry points. Older `tls-client` builds may
not, in which case the streaming methods throw `EntryPointNotFoundException`
on first use:

```csharp
if (!NativeTlsClient.IsStreamingSupported)
    throw new NotSupportedException("Loaded native library does not expose the streaming entry points.");
```

---

## 🚀 Quick Start

### Server-Sent Events

```csharp
using TlsClient.Native;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
using System.Text;

NativeTlsClient.Initialize("{PATH_TO_NATIVE_LIBRARY}");

using var client = new NativeTlsClient(new TlsClientOptions(
    TlsClientIdentifier.Chrome133,
    "Mozilla/5.0 ... Chrome/133")
{
    // ⚠️ A negative duration disables the native http.Client.Timeout entirely.
    // Required for long-lived SSE streams. TimeSpan.Zero does NOT work — it
    // is treated as "use the 30 s default" by the native side.
    Timeout = System.Threading.Timeout.InfiniteTimeSpan,
});

var start = client.RequestStream(new Request
{
    RequestUrl = "https://example.com/sse",
    RequestMethod = HttpMethod.Get,
    Headers = new() { ["Accept"] = "text/event-stream" },
});

try
{
    var sb = new StringBuilder();
    var utf8 = Encoding.UTF8.GetDecoder(); // keeps a character that spans two chunks
    while (true)
    {
        var chunk = client.ReadStream(start.StreamId, timeoutMs: 1000);

        if (!string.IsNullOrEmpty(chunk.Error))
            throw new IOException($"stream error: {chunk.Error}");

        if (chunk.Timeout)
        {
            // No data for 1s. Check your CancellationToken here, then continue.
            ct.ThrowIfCancellationRequested();
            continue;
        }

        // Decode with one Decoder for the whole stream: a multi-byte UTF-8 character
        // (ä, €, emoji) can be split across two chunks, and decoding each chunk on
        // its own would turn both halves into U+FFFD.
        byte[] bytes = chunk.GetChunkBytes();
        char[] chars = new char[utf8.GetCharCount(bytes, 0, bytes.Length, flush: chunk.EOF)];
        utf8.GetChars(bytes, 0, bytes.Length, chars, 0, flush: chunk.EOF);
        sb.Append(chars);

        // Pull complete SSE events (`\n\n`-separated) out of the buffer:
        int sep;
        while ((sep = sb.ToString().IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
        {
            string ev = sb.ToString(0, sep);
            sb.Remove(0, sep + 2);
            ProcessEvent(ev);
        }

        if (chunk.EOF) break;
    }
}
finally
{
    client.CancelStream(start.StreamId); // idempotent
}
```

### "I don't know upfront if it's streaming"

When the same code path may receive either an SSE response *or* a regular JSON
body, branch on `Content-Type` from the headers `RequestStream` returns:

```csharp
var start = client.RequestStream(req);

string ct = start.Headers?.GetValueOrDefault("Content-Type")?.FirstOrDefault() ?? "";
if (ct.Contains("text/event-stream") || ct.Contains("application/x-ndjson"))
{
    // Streaming — loop ReadStream as in the SSE example above.
}
else
{
    // Not streaming — drain the body in one extra FFI call.
    Response full = client.ReadStreamAll(start.StreamId);
    Console.WriteLine(full.Body);
}
```

The non-streaming branch costs **one extra FFI call** vs. plain `Request()` —
negligible compared to the network round-trip and avoids the "predict ahead of
time" problem.

---

## 🔁 The `timeoutMs` parameter on `ReadStream`

| Value     | Behaviour                                                                                  |
| --------- | ------------------------------------------------------------------------------------------ |
| `< 0`     | Block until the next chunk, EOF, or error.                                                 |
| `= 0`     | Non-blocking poll — returns `Timeout=true` immediately if no chunk is buffered.            |
| `> 0`     | Block up to `timeoutMs` ms, then return `Timeout=true` if no chunk arrived (heartbeat).    |

`Timeout=true` is **not** an error — it just means "no data yet." Use it to
periodically check a `CancellationToken` from your read loop without blocking
the underlying P/Invoke thread indefinitely.

---

## 🧩 Single-thread tunneling

A common pattern with `TlsClient.Native` is to serialize every native call
through a single dedicated thread (to keep the cgo boundary simple). The
streaming API fits that model:

* `ReadStream` with `timeoutMs >= 0` and `CancelStream` return quickly.
  `RequestStream` blocks until the response headers arrive, `ReadStream` with
  a negative `timeoutMs` until the next chunk, and `ReadStreamAll` until the
  whole body is in — for an endless SSE stream that is never. Don't make those
  calls on a shared dispatcher for long-lived streams.
* The Go side keeps a per-stream goroutine that pumps body bytes into a
  buffered channel — `ReadStream` just pops the next chunk.
* You can safely interleave streaming reads with normal `Request()` calls on
  the same dispatcher.

---

## ⚙️ Tuning

| Knob | Where | Effect |
| ---- | ----- | ------ |
| `Request.StreamOutputBlockSize` | per request | Per-Read buffer size on the Go side. Defaults to **4096 bytes**. SSE rarely benefits from making it smaller; large binary streams may benefit from `64 * 1024`. |
| `ReadStream(streamId, timeoutMs)` | per call | How often the read loop wakes up to check cancellation. Typical: **250–1000 ms**. |
| `TlsClientOptions.Timeout` | per client | ⚠️ **Set to `Timeout.InfiniteTimeSpan` (or any negative duration) for long-lived streams.** Go's `http.Client.Timeout` covers the entire request *including* body reads, so the default of 60 s will tear down a long-lived SSE stream. `TimeSpan.Zero` is treated as "use the native 30 s default" — only a negative value disables the deadline. Each `NativeTlsClient` has its own native session, and the timeout is fixed by the first request of that session: a per-request `Request.TimeoutMilliseconds` only takes effect on that first request. Use a dedicated client for streaming. |

---

## 📖 Reference (returned types)

### `StreamStartResponse`
Extends `Response` with one extra field: `Guid StreamId`. `Body` is always empty.

### `StreamChunkResponse`
| Field        | Meaning                                                                   |
| ------------ | ------------------------------------------------------------------------- |
| `StreamId`   | Echo of the input id.                                                     |
| `Chunk`      | Base64-encoded raw bytes (decompressed). Empty on EOF / Timeout / Error.  |
| `EOF`        | `true` once. Stream is closed; `StreamId` is invalid after this.          |
| `Timeout`    | `true` when no data arrived within `timeoutMs`. Stream still live.        |
| `Error`      | Non-empty on a fatal read error. Stream is closed.                        |
| `IsTerminal` | Convenience: `EOF || !string.IsNullOrEmpty(Error)`.                       |
| `GetChunkBytes()` | `Convert.FromBase64String(Chunk)` with a null-safe fallback.         |

`CancelStream` returns the existing `DestroyResponse` (`{ Id, Body, Success }`).

---

## 🚧 Limitations

* **No callbacks across the FFI boundary** — the model is poll-based, by
  design. Reverse P/Invoke into managed code from arbitrary Go goroutine
  threads is fragile and would break the single-thread tunneling pattern.
* **Backpressure timeout** — the Go side buffers up to 64 chunks per stream. If
  that buffer is full and you stop calling `ReadStream` for 60 s, the stream is
  torn down: after the chunks already buffered, `ReadStream` returns an `Error`
  starting with `stream aborted` (and `ReadStreamAll` fails), because the body
  is incomplete. It never looks like a normal `EOF`.
* **Abandoned streams** — once the body has ended, a stream that is not read
  for 5 minutes is discarded; later reads return `Error: unknown streamId`.
  Call `CancelStream` when you stop reading early.
* **One reader per stream** — concurrent `ReadStream` calls on the same
  `StreamId` are not coordinated and produce undefined chunk ordering.
  Don't do it.
* **Cancellation** — the `CancellationToken` of the `…Async` methods is only
  checked before the native call starts; a call that is already waiting in the
  native library is not interrupted. Use a `timeoutMs` heartbeat instead.
* **`WithDebug`** — with `TlsClientOptions.WithDebug = true` the native side
  reads the whole body before `RequestStream` returns, to log it, so the
  response is not streamed.

---

## 🧯 Troubleshooting

| Symptom                                      | Likely cause                                                                                  |
| -------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `EntryPointNotFoundException: requestStream` | The loaded native library predates the streaming exports. Use a v1.11.2-stream or later DLL. |
| Stream ends after exactly 60 s (or 30 s)     | The deadline of `TlsClientOptions.Timeout` (60 s by default; the native 30 s default when it is `TimeSpan.Zero`) covers the body read. Set `TlsClientOptions.Timeout = Timeout.InfiniteTimeSpan` on the client you stream with. A `Request.TimeoutMilliseconds = -1` on a single request only helps if it is the first request of that client's session. |
| Endless `Timeout=true` heartbeats            | The remote side hasn't flushed any bytes yet. Verify the endpoint actually streams.           |
| `Error: unknown streamId`                    | You called `ReadStream` after `EOF` / `Error` / `CancelStream`. Stop the loop on those.       |
