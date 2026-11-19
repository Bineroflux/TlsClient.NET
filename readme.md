# TlsClient.Net

A **.NET wrapper** around [bogdanfinn/tls-client](https://github.com/bogdanfinn/tls-client).
Brings advanced TLS fingerprinting, browser emulation, User-Agent control, header ordering, cookie handling, certificate validation toggles, and proxy support to .NET.

---

## 📦 Packages

* **Native**

  * **[`TlsClient.Native`](./src/TlsClient.Native/README.md)** — calls the native `tls-client` library directly (no remote service).
* **Remote Service**

  * **[`TlsClient.Api`](./src/TlsClient.Api/README.md)** — talks to a running `tls-client` HTTP service.

* **Providers**
  * **[`TlsClient.HttpClient`](./src/providers/TlsClient.Provider.HttpClient/README.md)** — drop-in `HttpClientHandler` replacement.
  * **[`TlsClient.RestSharp`](./src/providers/TlsClient.Provider.RestSharp/README.md)** — integration with RestSharp client.


> ℹ️ The shared models and builders commonly referred to as “Core” (e.g., `Request`, `Response`, `TlsClientOptions`, `TlsClientIdentifier`, helpers) are **included within each package**. You do **not** install a separate `TlsClient.Core` package.

---

## 🧭 Which one should I use?

* **Use `TlsClient.Native`** if you want everything **in-process** and can ship the native library with your app.
  **Note:** Due to C# ↔ Go native interop, some environments may experience instability or edge-case issues (GC/pinning, P/Invoke marshalling, native memory management). If you hit odd crashes or hangs, prefer the API mode below.
* **Use `TlsClient.Api`** if you want a **separate service** (local Docker/remote host) and a thin .NET client in your app. This avoids Go interop inside your process and is generally more robust operationally.

Both packages expose the same request/response shapes.

---

## ⚙️ Installation

### Option A — Native (in-process)

1. Add the package:

```bash
dotnet add package TlsClient.Native
```

2. Add the **runtime-specific native binary** for your target platform:

| OS             | Arch  | NuGet package                         |
| -------------- | ----- | ------------------------------------- |
| Windows        | x64   | `TlsClient.Native.win-x64`            |
| Windows        | x86   | `TlsClient.Native.win-x32`            |
| Linux          | amd64 | `TlsClient.Native.linux-amd64` (up to 1.15.1: `TlsClient.Native.linux-ubuntu-amd64`) |
| Linux          | arm64 | `TlsClient.Native.linux-arm64`        |
| Linux          | armv7 | `TlsClient.Native.linux-armv7`        |
| Linux (Alpine) | amd64 | `TlsClient.Native.linux-alpine-amd64` |
| macOS (Apple)  | arm64 | `TlsClient.Native.darwin-arm64`       |
| macOS (Intel)  | amd64 | `TlsClient.Native.darwin-amd64`       |

To run one build on several operating systems, reference one platform package per OS, e.g. `TlsClient.Native.win-x64` and `TlsClient.Native.linux-amd64`. A portable (framework-dependent, no RID) build then contains `runtimes/win-x64/native/tls-client.dll` and `runtimes/linux-x64/native/tls-client.so`, and `NativeTlsClient.Initialize(null)` loads the one for the OS the app runs on. A RID-specific publish (`dotnet publish -r linux-x64`) copies only the matching library next to the app.

3. Initialize once at startup:

```csharp
using TlsClient.Native;

NativeTlsClient.Initialize("{PATH_TO_NATIVE_LIBRARY}");
// e.g. "C:\\tools\\tls-client\\tls-client-windows-64-1.11.0.dll"
```

> `TlsClient.Native` requires **.NET 8 or later**.
> A relative path passed to `Initialize` is resolved against the current directory. With a platform package referenced, `NativeTlsClient.Initialize(null)` finds the bundled library on its own: the platform packages ship it at the standard `runtimes/<rid>/native/` location, so it also ends up in `dotnet publish` output. Platform packages in the former `runtimes/tls-client/<os>/<arch>/` layout are still found; older versions of `TlsClient.Native` itself, however, do not find the library in platform packages with the new layout.

> ⚠️ Without `NativeTlsClient.Initialize(...)`, native mode won’t work.

**Interop note (important):** C# calling into the Go native library can be less stable on some systems (especially under heavy concurrency). If you encounter flakiness (random SIG… signals, heap issues, or unexplained timeouts), switch to `TlsClient.Api`.

---

### Option B — API (service)

If you run a `tls-client` HTTP service (local/remote):

```bash
dotnet add package TlsClient.Api
```

No native init required; just point to the service URL and provide an API key.

---

## 🚀 Quick Start

### Native (in-process)

```csharp
using TlsClient.Native;
using TlsClient.Core.Models.Requests; // bundled inside package

NativeTlsClient.Initialize("{PATH_TO_NATIVE_LIBRARY}");

using var client = new NativeTlsClient();
var res = client.Request(new Request { RequestUrl = "https://httpbin.io/get" });
Console.WriteLine(res.Status);
```

### API (service)

```csharp
using TlsClient.Api;
using TlsClient.Core.Models.Requests; // bundled inside package

using var client = new ApiTlsClient(new Uri("http://127.0.0.1:8080"), "my-auth-key-1");
var res = client.Request(new Request { RequestUrl = "https://httpbin.io/get" });
Console.WriteLine(res.Status);
```

> For additional scenarios, please refer to the test projects in each package.

---

## 🔀 Streaming Responses

`TlsClient.Native` supports consuming streaming responses (Server-Sent Events,
NDJSON, chunked bodies) without buffering the whole body — through a
`RequestStream` / `ReadStream` / `CancelStream` API on `NativeTlsClient`.

See [docs/streaming.md](./docs/streaming.md) for the full guide, including SSE
examples, `Content-Type`-based branching, tuning knobs, and limitations.

---

## 🔌 WebSockets

`TlsClient.Native` opens WebSocket connections with the client profile's TLS
fingerprint — through a `WsConnect` / `WsRead` / `WsWrite` / `WsClose` API on
`NativeTlsClient`.

See [docs/websockets.md](./docs/websockets.md) for the guide, including header
rules, the read loop, and limitations.

---

## 🧯 Support & Issues

* Wrapper/packaging issues → open an issue **here**.
* Native TLS engine issues → [bogdanfinn/tls-client](https://github.com/bogdanfinn/tls-client).

---

## 🔬 Learn More

This focuses on setup.
For **additional scenarios**, please refer to the test projects.

---

## 📜 License

Licensed under the **MIT License**.
See [LICENSE](./LICENSE) for details.

---

## ©️ Copyright

**© 2025 TlsClient.NET** — Maintained by **Eren Kurt**

* GitHub: [@ErenKrt](https://github.com/ErenKrt)
* Instagram: [@ep.eren](https://instagram.com/ep.eren)
