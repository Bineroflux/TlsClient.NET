using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using TlsClient.Core.Models.Entities;
using TlsClient.Core.Models.Requests;
using TlsClient.Native;

Console.WriteLine($"OS:  {RuntimeInformation.OSDescription}");
Console.WriteLine($"RID: {RuntimeInformation.RuntimeIdentifier}, .NET {Environment.Version}");

if (Environment.GetEnvironmentVariable("DEMO_FIRSTCHANCE") == "1")
    AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        Console.WriteLine($"       first-chance {e.Exception.GetType().Name}: {e.Exception.Message} @ {e.Exception.StackTrace?.Split('\n')[0].Trim()}");

// DEMO_NRE=1: diagnostics for NullReferenceException handling with the Go library loaded.
if (Environment.GetEnvironmentVariable("DEMO_NRE") == "1")
{
    if (Environment.GetEnvironmentVariable("DEMO_SKIP_INIT") != "1")
        NativeTlsClient.Initialize(null);
    Console.WriteLine($"NRE test, Go library loaded: {Environment.GetEnvironmentVariable("DEMO_SKIP_INIT") != "1"}");
    Console.WriteLine($"  main thread, before any Go call: {Nre.Probe()}");
    Console.WriteLine($"  pool thread, before any Go call: {await Task.Run(Nre.Probe)}");
    if (Environment.GetEnvironmentVariable("DEMO_SKIP_INIT") != "1")
    {
        using (var c = new NativeTlsClient(new TlsClientOptions(TlsClientIdentifier.Chrome154, "nre")))
            Console.WriteLine($"  Go request on main thread -> {(int)c.Request(new Request { RequestUrl = "https://httpbin.org/get" }).Status}");
        Console.WriteLine($"  main thread, after a Go call:    {Nre.Probe()}");
        Console.WriteLine($"  pool thread, after a Go call:    {await Task.Run(Nre.Probe)}");
    }
    Console.WriteLine(Nre.Failures == 0 ? "NRE test done, ALL OK" : $"NRE test done, {Nre.Failures} FAILURE(S)");
    return Nre.Failures == 0 ? 0 : 1;
}

// No path: the runtime resolver finds runtimes/<rid>/native/tls-client.{dll,so} itself.
NativeTlsClient.Initialize(null);
Console.WriteLine($"Streaming supported: {NativeTlsClient.IsStreamingSupported}");

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {what}");
    if (!ok) failures++;
}

var options = new TlsClientOptions(TlsClientIdentifier.Chrome154, "Mozilla/5.0 CrossOsDemo")
{
    Timeout = TimeSpan.FromSeconds(30),
    DisableHttp3 = Environment.GetEnvironmentVariable("DEMO_DISABLE_H3") == "1",
    WithDebug = Environment.GetEnvironmentVariable("DEMO_GO_DEBUG") == "1",
};

// DEMO_ONLY=stream|request2 runs a single step (diagnostics); DEMO_STREAM_TIMEOUT=<s> gives the
// streaming client a finite timeout instead of an infinite one.
string only = Environment.GetEnvironmentVariable("DEMO_ONLY") ?? "";
int requestRuns = only == "request2" ? 2 : only == "" ? 1 : 0;

// 1) Plain request; tls.peet.ws echoes the TLS fingerprint it saw.
for (int run = 0; run < requestRuns; run++)
using (var client = new NativeTlsClient(options))
{
    var res = client.Request(new Request { RequestUrl = "https://tls.peet.ws/api/clean" });
    Check(res.Status == HttpStatusCode.OK, $"GET tls.peet.ws -> {(int)res.Status}");
    Console.WriteLine($"       {res.Body.ReplaceLineEndings(" ")[..Math.Min(160, res.Body.Length)]}");
}

// 2) Streaming request.
bool trace = Environment.GetEnvironmentVariable("DEMO_TRACE") == "1";
var clock = Stopwatch.StartNew();
void Trace(string msg) { if (trace) Console.WriteLine($"       [{clock.ElapsedMilliseconds,6} ms] {msg}"); }
var streamTimeout = int.TryParse(Environment.GetEnvironmentVariable("DEMO_STREAM_TIMEOUT"), out var st)
    ? TimeSpan.FromSeconds(st) : Timeout.InfiniteTimeSpan;
if (only is "" or "stream")
using (var client = new NativeTlsClient(new TlsClientOptions(TlsClientIdentifier.Chrome154, "CrossOsDemo") { Timeout = streamTimeout, WithDebug = options.WithDebug }))
{
    Trace($"requestStream -> (timeout {streamTimeout})");
    var start = client.RequestStream(new Request { RequestUrl = "https://httpbin.org/stream/5" });
    Trace($"requestStream <- {(int)start.Status} id={start.StreamId}");
    Check(start.Status == HttpStatusCode.OK, $"requestStream httpbin.org/stream/5 -> {(int)start.Status}");
    var body = new StringBuilder();
    try
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(60)) { Check(false, "stream did not finish within 60 s"); break; }
            var chunk = client.ReadStream(start.StreamId, 5000);
            Trace($"readStream <- chunk={chunk.Chunk?.Length ?? -1} eof={chunk.EOF} timeout={chunk.Timeout} error={chunk.Error}");
            if (!string.IsNullOrEmpty(chunk.Error)) { Check(false, $"readStream error: {chunk.Error}"); break; }
            if (chunk.Chunk != null) body.Append(Encoding.UTF8.GetString(chunk.GetChunkBytes()));
            if (chunk.EOF) break;
        }
    }
    finally
    {
        client.CancelStream(start.StreamId);
    }
    int lines = body.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    Check(lines == 5, $"streamed {lines} events");
}

// 3) Concurrency + GC pressure against a local server (no rate limits): many parallel
// requests on separate sessions, so Go threads, .NET threads and the GC run side by side.
if (only == "")
{
    var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    int port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    using var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");
    listener.Start();
    _ = Task.Run(async () =>
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); } catch { return; }
            _ = Task.Run(() =>
            {
                var bytes = Encoding.UTF8.GetBytes("{\"path\":\"" + ctx.Request.Url!.PathAndQuery + "\"}");
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(bytes);
                ctx.Response.Close();
            });
        }
    });

    const int total = 2000;
    var sw = Stopwatch.StartNew();
    int ok = 0;
    using var gate = new SemaphoreSlim(32);
    var tasks = Enumerable.Range(0, total).Select(async i =>
    {
        await gate.WaitAsync();
        try
        {
            using var client = new NativeTlsClient(options);
            var res = await client.RequestAsync(new Request { RequestUrl = $"http://localhost:{port}/get?i={i}" });
            if (i % 50 == 0) GC.Collect();
            if (res.Status == HttpStatusCode.OK && res.Body.Contains($"i={i}\"")) Interlocked.Increment(ref ok);
            else Console.WriteLine($"       request {i}: {(int)res.Status} {res.Body[..Math.Min(200, res.Body.Length)].ReplaceLineEndings(" ")}");
            client.Destroy();
        }
        finally { gate.Release(); }
    });
    await Task.WhenAll(tasks);
    Check(ok == total, $"{total} requests (32 parallel, local server), {ok} OK in {sw.ElapsedMilliseconds} ms");
}

Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

static class Nre
{
    sealed class Box { public int Value = 42; }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Read(Box? b) => b!.Value; // faulting load -> SIGSEGV -> NullReferenceException

    public static int Failures;

    public static string Probe()
    {
        try { Read(null); Interlocked.Increment(ref Failures); return "FAIL: no exception"; }
        catch (NullReferenceException) { return "NullReferenceException caught OK"; }
    }
}
