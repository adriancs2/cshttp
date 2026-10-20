# cshttp

**A Zero-Dependency, Modular HTTP/1.1 Stack & Standalone Web Server for C#.**  
**Runs identically on `.NET Framework 4.8` and `.NET 8 / 10+` — No IIS. No Kestrel. Zero NuGet Packages.**

---

## 1. Why This Project Exists & The Problems It Solves

In systems programming languages, the boundary between the network socket, the wire-level HTTP parser, the web content toolkit, and the application server is modular and explicit. C has `llhttp`, Rust has `httparse`, and Go has a standard library `net/http` server that compiles into a single self-contained binary.

Historically, C# developers faced two painful gaps:

1. **The All-or-Nothing Framework Tax:** If you wanted to parse HTTP/1.1 or listen for HTTP requests in .NET without adopting the entirety of ASP.NET (`System.Web` + IIS) or ASP.NET Core (`Microsoft.AspNetCore.*` + Kestrel + DI + Middleware pipelines), your only choices were the Windows-bound `HttpListener` (`http.sys`) or a fragile hand-rolled string splitter that silently breaks on chunked transfer encoding, HTTP request smuggling, bare CR injection, or multipart file uploads.
2. **Missing Synchronous, Classic `System.Web` Ergonomics Outside IIS:** Many C# developers prefer straightforward, synchronous request handling (`HttpContext.Current`, `Request.QueryString`, `Request.Form`, `Request.Files`, `Response.Write`, and a simple `switch` router) over complex DI containers, controller reflection, and viral `async/await` pipelines. Until now, getting that clean `System.Web`-style developer experience in a self-contained `.exe` across both `.NET Framework 4.8` and `.NET 8/10+` was impossible.

**`cshttp` solves both problems.** Written in pure **C# 7.3** with **zero external dependencies** and a single namespace (`cshttp`), it provides a clean 3-tier modular HTTP/1.1 stack that you can embed as source files or reference as lightweight class libraries.

---

## 2. Repository Overview & Which Layer Do You Need?

This repository is organized into **three modular library tiers** (`src/`) and a **comprehensive verification & benchmark suite** (`tests/`):

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                         YOUR C# APPLICATION CODE                             │
│              (Simple Switch Router + Synchronous C# Handlers)                │
└───────────────────────────────────────┬──────────────────────────────────────┘
                                        │
                                        ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ TIER 3: cshttp.server  (3 .cs files — src/cshttp.server/)                    │
│  • Standalone HTTP/1.1 & HTTPS TCP Server (Run as Console Exe or OS Service) │
│  • Zero-Thread IOCP Idle Keep-Alive + Synchronous Worker Burst Loop          │
│  • Slowloris Defense, TrustedProxies (X-Forwarded-*), Graceful Drain         │
└───────────────────────────────────────┬──────────────────────────────────────┘
                                        │
                                        ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ TIER 2: cshttp.web     (11 .cs files — src/cshttp.web/)                      │
│  • Drop-In System.Web Surface: HttpContext.Current, HttpRequest, HttpResponse│
│  • Lazy-Parsed QueryString, Form, Cookies, Multipart Files, HttpUtility      │
│  • StaticFiles (ETag, 304, Byte Ranges 206, Path-Traversal Guard), MapPath   │
└───────────────────────────────────────┬──────────────────────────────────────┘
                                        │
                                        ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│ TIER 1: cshttp         (7 .cs files — src/cshttp/)                           │
│  • Pure RFC 9112 Wire Engine: Resumable Request-Head Parser (No I/O)         │
│  • Zero-Copy ChunkedDecoder, Response Head Writer, Smuggling & CRLF Guards   │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Architectural Decision Guide: Choosing Your Tier

| Component | What It Is & What It Unlocks | When You Need ONLY This Tier |
| :--- | :--- | :--- |
| **`cshttp`**<br>*(Tier 1 — 7 files)* | Pure byte-level RFC 9112 HTTP/1.1 request-head parser, incremental zero-copy `ChunkedDecoder`, and `HttpResponseWriter`. Performs **zero socket I/O** and allocates nothing until a full header block arrives. | Choose **`cshttp` only** when you are building a **custom TCP reverse proxy, API gateway, packet analyzer, traffic recorder, or embedded socket loop** where you already manage raw `byte[]` buffers and only need strict RFC 9112 framing and request-smuggling protection. |
| **`cshttp.web`**<br>*(Tier 2 — 11 files)* | Full content toolkit and `System.Web`-compatible object model (`HttpContext.Current`, `HttpRequest`, `HttpResponse`, lazy `QueryString` / `Form` / `Files` / `Cookies`, `StaticFiles`, `HostingEnvironment`, `HttpUtility`). | Choose **`cshttp` + `cshttp.web`** when you want rich, lazy-evaluated form/multipart/cookie parsing and `System.Web` handler ergonomics **over your own custom transport** (e.g., custom async socket pipelines, named pipes, or **in-memory unit testing of web handlers without opening a TCP port**). |
| **`cshttp.server`**<br>*(Tier 3 — 3 files)* | Production standalone HTTP/1.1 & HTTPS server engine using **Zero-Thread IOCP Idle Keep-Alive + Synchronous Worker Execution** (capable of **62,000+ RPS** with 1,500 concurrent connections on a 4-core CPU). | Choose the **full `cshttp.server` stack** when building a **self-contained C# web application, API server, microservice, or embedded web UI** that compiles into a standalone `.exe` and runs on Windows or Linux without IIS or Kestrel. |
| **`tests/`**<br>*(Tests, Benchmarks & Demo App)* | • [`tests/cshttp.Tests`](./tests/cshttp.Tests/): 95 RFC, security, socket, and seeded fuzz tests.<br>• [`tests/benchmark-test`](./tests/benchmark-test/): Concurrency stress-test suite.<br>• [`tests/test-hearth-file-manager`](./tests/test-hearth-file-manager/): Full production web application running on `cshttp.server`. | Use these projects to verify RFC compliance on `.NET 4.8` and `.NET 10`, reproduce our concurrency benchmark numbers, or explore a complete real-world application built on `cshttp`. |

---

## 3. Tier 1 Summary: `cshttp` (The Pure Wire Engine)

`cshttp` turns raw `byte[]` slices into a validated `HttpRequestHead` and serializes `HttpHeaders` back into wire bytes. It is resumable across partial socket reads, enforces all RFC 9112 request-smuggling rules (rejecting conflicting `Transfer-Encoding` + `Content-Length`, bare `CR`, `obs-fold`, and header injection), and decodes `Transfer-Encoding: chunked` streams with zero intermediate buffer copies.

```csharp
using cshttp;

// Resumable, allocation-free until the blank line (\r\n\r\n) arrives:
HttpRequestHead head;
HttpParseResult r = HttpRequestParser.ParseHead(buffer, start, end - start, ParserOptions.Shared, out head);

if (r.Status == HttpParseStatus.Complete)
{
    start += r.Consumed; // Exact byte offset where body (or next pipelined request) begins
    string method = head.Method;          // "POST"
    string path   = head.Path;            // "/api/upload"
    bool keep     = head.KeepAlive;       // true
    HttpBodyKind kind = head.BodyKind;    // None, ContentLength, or Chunked
}
else if (r.Status == HttpParseStatus.Error)
{
    // r.ErrorStatusCode tells the server whether to answer 400, 414, 417, 431, 501, or 505
}
```

👉 **[Read the Deep-Dive Specification & Code Examples for `cshttp` (Tier 1 Wiki)](./wiki/1-cshttp-Wire-Engine.md)**

---

## 4. Tier 2 Summary: `cshttp.web` (Content Toolkit & `System.Web` Drop-In)

`cshttp.web` wraps a parsed `HttpRequestHead` and body `byte[]` into the familiar `HttpContext`, `HttpRequest`, and `HttpResponse` objects. Every content collection (`QueryString`, `Form`, `Files`, `Cookies`) is **lazy-parsed on first access**, and uploaded `HttpPostedFile` streams read directly from the underlying request buffer without copying memory.

```csharp
using cshttp;

static void HandleUpload()
{
    HttpContext ctx      = HttpContext.Current;
    HttpRequest Request  = ctx.Request;
    HttpResponse Response = ctx.Response;

    // Unified indexer searches QueryString -> Form -> Cookies -> ServerVariables
    string folder = Request["folder"] ?? "default";

    // Lazy-parsed RFC 7578 multipart/form-data file upload
    HttpPostedFile file = Request.Files["file"];
    if (file != null && file.ContentLength > 0)
    {
        string dest = HostingEnvironment.MapPath("~/App_Data/" + System.IO.Path.GetFileName(file.FileName));
        file.SaveAs(dest);
    }

    Response.Cookies.Add(new HttpCookie("sid", "xyz") { HttpOnly = true, SameSite = SameSiteMode.Lax });
    Response.ContentType = "application/json";
    Response.Write("{\"uploaded\":true}");
}
```

👉 **[Read the Deep-Dive Specification & Code Examples for `cshttp.web` (Tier 2 Wiki)](./wiki/2-cshttp-web-Toolkit.md)**

---

## 5. Tier 3 Summary: `cshttp.server` 1.0 (Standalone High-Concurrency Server)

`cshttp.server` 1.0 is a self-contained HTTP/1.1 & HTTPS server that binds directly to TCP sockets. It uses a **Zero-Thread IOCP Idle State** while connections wait for their first byte or sit in HTTP Keep-Alive, and immediately hands off active requests to a **Synchronous Worker Burst Loop** so application handlers remain 100% synchronous (`Action<HttpContext>`) with zero `async/await` viral infection.

```csharp
using System;
using System.Net;
using cshttp;

public static class Program
{
    public static void Main()
    {
        var options = new ServerOptions
        {
            ContentRoot = AppDomain.CurrentDomain.BaseDirectory,
            MaxConnections = 10000,
            MaxRequestBodySize = 30L * 1024 * 1024
        };
        options.ListenHttp(IPAddress.Loopback, 8080);

        new HttpServer(options).Run(ctx => // Blocks until Ctrl+C / SIGTERM, then drains gracefully
        {
            string path = ctx.Request.Path.ToLowerInvariant().TrimEnd('/');

            if (StaticFiles.TryServe(ctx, "/css/", "css")) return;
            if (StaticFiles.TryServe(ctx, "/js/", "js")) return;

            switch (path)
            {
                case "":
                case "/hello":
                    ctx.Response.Write("Hello from cshttp.server 1.0!");
                    return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.Write("404 Not Found");
        });
    }
}
```

### Concurrency Stress-Test Highlights (1,500 Idle Keep-Alive Connections + 50 Active Clients)
*Tested on an Intel Core i7-4770S (4 Cores / 8 Threads @ 3.10 GHz, 24 GB RAM):*

| Server Architecture Strategy | Idle Conns Accepted | Active Throughput (`.NET 10`) | Active Throughput (`.NET 4.8`) | `p99` Latency (`.NET 10`) | OS Threads Added by Idle Conns |
| :--- | :---: | ---: | ---: | ---: | :---: |
| **`cshttp.server.alpha.1.0`** (1 Thread/Conn, `MaxConn=1000`) | 1,000 / 1,500 ❌ | **0 req/s** ❌ | **0 req/s** ❌ | — | +1,001 |
| **`cshttp.server.alpha.1.0`** (1 Thread/Conn, `MaxConn=2000`) | 1,500 / 1,500 | 12,997 req/s | 7,444 req/s | 36.10 ms | +1,500 |
| **Paper Reference Model** (`AcceptAsync` + `Task.Run` + `Async I/O`) | 1,500 / 1,500 | 31,758 req/s | 32,546 req/s | 11.12 ms | +0 |
| **Official `cshttp.server` 1.0** (0-Thread IOCP Idle + Sync Worker) | **1,500 / 1,500** ✅ | **62,031 req/s** 🏆 | **49,966 req/s** 🏆 | **6.32 ms** 🏆 | **+1** 🏆 |

👉 **[Read the Deep-Dive Specification & Code Examples for `cshttp.server` (Tier 3 Wiki)](./wiki/3-cshttp-server-Engine.md)**  
👉 **[Read the Full Architecture Stress-Test & Benchmark White Paper](./wiki/4-Benchmark-Report.md)**

---

## 6. Real-World Application Showcase: Hearth File Manager

See [`tests/test-hearth-file-manager/`](./tests/test-hearth-file-manager/) for a complete, non-trivial application running on `cshttp.server` as a self-contained `.exe`:

* **Features:** Multi-user web file manager with chunked drag-and-drop uploads, zip/unzip streaming, static asset caching (`ETag` / `304`), cookie-based session authentication, and an integrated Gemini AI website builder.
* **Architecture:** Uses a clean `switch (path)` router in `Program.cs`, synchronous page/API handler classes using `HttpContext.Current`, `Request.Form`, `Request.Files`, and `StaticFiles.TryServe`, backed by an automated 46-check end-to-end HTTP test suite (`e2e-test.ps1`).

---

## 7. Building & Running the Test and Benchmark Suites

```bash
# 1. Build the entire solution in Release mode (net48 + net8.0/net10.0)
dotnet build cshttp.sln -c Release

# 2. Run the 95 RFC compliance, security, socket, and fuzz tests on .NET 10 and .NET Framework 4.8
dotnet run --project tests/cshttp.Tests/cshttp.Tests.csproj -c Release -f net10.0
dotnet run --project tests/cshttp.Tests/cshttp.Tests.csproj -c Release -f net48

# 3. Run the concurrency & architecture stress-test benchmark on .NET 10 and .NET Framework 4.8
dotnet run --project tests/benchmark-test/cshttp.Benchmark.csproj -c Release -f net10.0
dotnet run --project tests/benchmark-test/cshttp.Benchmark.csproj -c Release -f net48
```

---

## 8. Documentation Index

* **[Tier 1 Wiki: `cshttp` Wire Engine Specification & Examples](./wiki/1-cshttp-Wire-Engine.md)**
* **[Tier 2 Wiki: `cshttp.web` Content Toolkit & `System.Web` Drop-In Guide](./wiki/2-cshttp-web-Toolkit.md)**
* **[Tier 3 Wiki: `cshttp.server` 1.0 Server Configuration, TLS & Nginx Deployment](./wiki/3-cshttp-server-Engine.md)**
* **[White Paper: HTTP Listener Strategies & Concurrency Benchmark Report](./wiki/4-Benchmark-Report.md)**

---

## License

Released into the **Public Domain** under **The Unlicense**. No rights reserved.
