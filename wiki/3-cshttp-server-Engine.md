# Tier 3 Deep Dive: `cshttp.server` 1.0 — High-Concurrency Standalone Web Server

> **Source Folder:** [`src/cshttp.server/`](../src/cshttp.server/)  
> **Namespace:** `cshttp`  
> **Dependencies:** References [`cshttp`](./1-cshttp-Wire-Engine.md) and [`cshttp.web`](./2-cshttp-web-Toolkit.md) (Zero external NuGet packages, C# 7.3, `.NET Framework 4.8` and `.NET 8 / 10+`)

---

## 1. Overview & Architecture

**`cshttp.server` 1.0** is a self-contained, production-ready HTTP/1.1 and HTTPS TCP server that embeds directly into any C# Console Application, Windows Service, or Linux `systemd` daemon.

As proven in our [Concurrency & Stress-Test Benchmark Report](./4-Benchmark-Report.md), `cshttp.server` 1.0 solves the classic tension between **idle keep-alive connection scalability** and **synchronous handler simplicity** by dividing the connection lifecycle into two distinct stages:

1. **Zero-Thread IOCP Idle State (`ArmIdleRead`):**
   * Whenever a newly accepted connection waits for its first request—or an existing HTTP/1.1 connection sits idle between keep-alive requests—`Connection` registers an asynchronous OS-level I/O Completion Port (IOCP / epoll) wait via `_stream.BeginRead(...)` and releases the thread immediately.
   * **10,000 idle browser keep-alive connections consume 0 OS threads.**
   * A single lightweight background timer (`SweepIdleConnections`, ticking every 200 ms) checks `Stopwatch` deadlines across all idle connections and aborts expired sockets without holding per-socket timers or threads.
2. **IOCP-to-Worker Hand-Off & Synchronous Burst Execution (`RunActiveWorker`):**
   * The instant the first packet of a request arrives, the IOCP callback completes `EndRead` and immediately queues `RunActiveWorker` to a pre-warmed `.NET ThreadPool` **Worker thread** (`ThreadPool.QueueUserWorkItem`), freeing the IOCP thread in nanoseconds so synchronous DB/file handlers never starve network I/O completions.
   * On the Worker thread, header parsing, body reading (with Slowloris minimum-data-rate enforcement), `HttpContext.Current` assignment, the application's synchronous `Action<HttpContext>` handler, and response flushing run straight through with **zero `async/await` state-machine allocations**.
   * Before releasing the Worker thread, `RunActiveWorker` checks `if (_socket.Available > 0)`. If the client (or an Nginx upstream keep-alive pool) has already sent a follow-up request, the same hot worker thread drains it immediately with **zero context switches**.

---

## 2. File Inventory (`src/cshttp.server/` — 3 Files)

| File | Public Types | Role |
| :--- | :--- | :--- |
| **[HttpServer.cs](../src/cshttp.server/HttpServer.cs)** | `HttpServer` | Socket listener bindings, pre-warmed `ThreadPool` configuration, active connection/request counters, 200 ms idle-timeout sweeper, and graceful shutdown drain (`Start`, `Run`, `Stop`). |
| **[Connection.cs](../src/cshttp.server/Connection.cs)** | `Connection` *(internal)* | Per-socket state machine: TLS handshake (`SslStream`), Zero-Thread IOCP idle wait, synchronous worker request loop, `100-continue`, chunked body decoding, Slowloris rate checking, `X-Forwarded-*` resolution, and socket linger drain. |
| **[ServerOptions.cs](../src/cshttp.server/ServerOptions.cs)** | `ServerOptions`, `ServerBinding` | Endpoints (`ListenHttp`, `ListenHttps`), connection caps, timeouts, Slowloris thresholds, trusted proxy IPs, and error hooks. |

---

## 3. Complete `ServerOptions` Reference

```csharp
using System;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using cshttp;

var options = new ServerOptions
{
    // --- Application Root (used by HostingEnvironment.MapPath & StaticFiles) ---
    ContentRoot = AppDomain.CurrentDomain.BaseDirectory,

    // --- Connection & Payload Limits ---
    MaxConnections = 10000,                       // Concurrent open sockets (idle + active); extra are closed immediately
    MaxRequestsPerConnection = 0,                 // 0 = unlimited keep-alive requests per connection
    MaxRequestBodySize = 30L * 1024 * 1024,       // 30 MB default (413 Content Too Large if exceeded)

    // --- Wire & Form Limits ---
    Parser = new ParserOptions
    {
        MaxRequestLineLength = 8 * 1024,          // 8 KB (414 URI Too Long)
        MaxHeaderBytes = 32 * 1024,               // 32 KB total header block (431)
        MaxHeaderCount = 100,                     // 100 headers max (431)
        AllowBareLf = false                       // Strict CRLF framing
    },
    Request = new RequestSettings
    {
        MaxCollectionKeys = 1000,                 // Max QueryString / Form keys (400 if exceeded)
        MaxMultipartParts = 1000,                 // Max multipart/form-data parts
        MaxMultipartHeaderBytes = 16 * 1024       // Max headers per multipart part
    },

    // --- Timeouts & Slowloris Defense ---
    KeepAliveTimeout = TimeSpan.FromSeconds(60),        // Idle wait between keep-alive requests
    RequestHeadersTimeout = TimeSpan.FromSeconds(30),   // Max time to receive full request headers (408 if exceeded)
    BodyReadTimeout = TimeSpan.FromSeconds(30),         // Max stall on any single body read (408)
    MinRequestBodyBytesPerSecond = 240,                 // Minimum upload rate after grace period (408 Slowloris defense)
    MinDataRateGracePeriod = TimeSpan.FromSeconds(5),   // Grace period before min body rate is enforced
    SendTimeout = TimeSpan.FromSeconds(60),             // Socket send timeout
    TlsHandshakeTimeout = TimeSpan.FromSeconds(10),     // Max time for SslStream.AuthenticateAsServer
    ShutdownTimeout = TimeSpan.FromSeconds(30),         // Graceful drain window on Stop() / Ctrl+C

    // --- Reverse Proxy & Security ---
    ServerHeader = null,                          // Omitted by default; set e.g. "my-server" if desired
    DetailedErrors = false,                       // Set true only in development to include stack traces in 500 pages
    SslProtocols = SslProtocols.None,             // Lets the OS negotiate TLS 1.2 / TLS 1.3

    // --- Diagnostics & Error Logging ---
    Log = msg => Console.WriteLine("[cshttp] " + msg),
    OnUnhandledException = (ctx, ex) => Console.Error.WriteLine("[500] " + ctx.Request.RawUrl + ": " + ex)
};

// Trust X-Forwarded-For and X-Forwarded-Proto from a local Nginx reverse proxy:
options.TrustedProxies.Add(IPAddress.Loopback);
options.TrustedProxies.Add(IPAddress.IPv6Loopback);

// Bind HTTP on loopback (behind Nginx) or IPAddress.Any (public/LAN):
options.ListenHttp(IPAddress.Loopback, 8080);

// Optional: Bind HTTPS directly with an X509Certificate2:
// var cert = new X509Certificate2("server.pfx", "password");
// options.ListenHttps(IPAddress.Any, 8443, cert);
```

---

## 4. Full Standalone Application Template (`Program.cs`)

Below is a complete, production-ready `Program.cs` showing how `cshttp.server` replaces `Global.asax.cs` and `web.config`:

```csharp
using System;
using System.Net;
using cshttp;

namespace MyStandaloneApp
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = new ServerOptions
            {
                ContentRoot = AppDomain.CurrentDomain.BaseDirectory,
                MaxRequestBodySize = 16L * 1024 * 1024,
                Log = msg => Console.WriteLine("[cshttp] " + msg),
                OnUnhandledException = (ctx, ex) => Console.Error.WriteLine("[error] " + ctx.Request.RawUrl + ": " + ex.Message)
            };
            options.TrustedProxies.Add(IPAddress.Loopback);
            options.ListenHttp(IPAddress.Loopback, 8080);

            // ---- Application_Start equivalent ----
            HostingEnvironment.SetApplicationPhysicalPath(options.ContentRoot);

            Console.WriteLine("Listening on http://localhost:8080/ (Press Ctrl+C to stop)");

            // Blocks until Ctrl+C, SIGTERM, or ProcessExit, then performs a graceful connection drain
            using (var server = new HttpServer(options))
            {
                server.Run(BeginRequest);
            }
            return 0;
        }

        // ---- Application_BeginRequest equivalent ----
        static void BeginRequest(HttpContext ctx)
        {
            HttpRequest Request = ctx.Request;
            HttpResponse Response = ctx.Response;

            Response.AppendHeader("X-Content-Type-Options", "nosniff");
            Response.AppendHeader("Referrer-Policy", "same-origin");

            string path = Request.Path.ToLowerInvariant().TrimEnd('/');

            // Serve static files if not offloaded to Nginx
            if (path.StartsWith("/css/") && StaticFiles.TryServe(ctx, "/css/", "css")) return;
            if (path.StartsWith("/js/") && StaticFiles.TryServe(ctx, "/js/", "js")) return;

            // Direct O(1) switch routing
            switch (path)
            {
                case "":
                case "/home":
                    Response.Write("<h1>Welcome to cshttp.server 1.0</h1>");
                    return;

                case "/api/status":
                    Response.ContentType = "application/json";
                    Response.Write("{\"ok\":true}");
                    return;
            }

            Response.StatusCode = 404;
            Response.ContentType = "text/plain";
            Response.Write("404 Not Found");
        }
    }
}
```

---

## 5. Graceful Shutdown & Zero-Downtime Draining

When `server.Stop()` is triggered (automatically hooked to `Console.CancelKeyPress` and `AppDomain.CurrentDomain.ProcessExit` inside `server.Run()`):

1. **Stop Listening:** All listening sockets are closed immediately so the OS stops routing new TCP handshakes to the process, and the idle-sweep timer is disposed.
2. **Close Idle Connections Immediately:** Every connection currently in the zero-thread `_idle == true` state is closed immediately via `c.CloseIfIdle()`.
3. **Drain In-Flight Requests:** Any connection currently executing inside `RunActiveWorker` is given up to `options.ShutdownTimeout` (default 30 seconds) to finish writing its HTTP response.
4. **Drain Background Work:** Remaining time in `ShutdownTimeout` is passed to `HostingEnvironment.InitiateShutdown(left)` to await any tasks queued via `HostingEnvironment.QueueBackgroundWorkItem(...)`.

---

## 6. Recommended Nginx Reverse-Proxy Configuration

When deploying `cshttp.server` in production behind Nginx (the 4-Pillar Stack), enable an upstream `keepalive` pool so Nginx reuses persistent loopback sockets to `cshttp.server`:

```nginx
upstream cshttp_backend {
    server 127.0.0.1:8080;
    keepalive 32;
}

server {
    listen 443 ssl http2;
    server_name example.com;

    # TLS 1.3 certificates managed by Nginx
    ssl_certificate     /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;

    # Optional: Serve static assets directly from Nginx via kernel sendfile
    location /css/    { root /var/www/myapp; expires 365d; }
    location /js/     { root /var/www/myapp; expires 365d; }
    location /assets/ { root /var/www/myapp; expires 365d; }

    # Forward dynamic routes over persistent HTTP/1.1 loopback connections
    location / {
        proxy_pass         http://cshttp_backend;
        proxy_http_version 1.1;
        proxy_set_header   Connection "";
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }
}
```
