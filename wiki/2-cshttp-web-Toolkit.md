# Tier 2 Deep Dive: `cshttp.web` — Content Toolkit & `System.Web` Drop-In

> **Source Folder:** [`src/cshttp.web/`](../src/cshttp.web/)  
> **Namespace:** `cshttp`  
> **Dependencies:** References only [`cshttp`](./1-cshttp-Wire-Engine.md) (Zero external NuGet packages, C# 7.3, `.NET Framework 4.8` and `.NET 8 / 10+`)

---

## 1. Overview & Design Philosophy

**`cshttp.web`** sits directly above the `cshttp` wire parser and provides the complete **application-facing HTTP object model and content toolkit**.

It serves two simultaneous goals:
1. **Lazy, High-Performance Content Parsing:** URL percent-decoding, query strings (`application/x-www-form-urlencoded`), `multipart/form-data` file uploads, `Cookie` / `Set-Cookie` headers, `Cache-Control` policies, and range-capable static file serving are evaluated **lazily on first access**. If a request handler only checks `Request.Path` and writes a JSON response, it pays **zero CPU or memory allocation cost** for parsing cookies, query strings, or form bodies.
2. **Familiar `System.Web`-Style API:** The core request/response surface (`HttpContext.Current`, `HttpRequest`, `HttpResponse`, `HttpCookie`, `HttpPostedFile`, `HttpUtility`, `HostingEnvironment.MapPath`) follows classic `System.Web` names and shapes, so Pageless ASP.NET handlers port with little effort. It is **not** a 1:1 mirror: Web Forms, Session, modules, and authentication are out of scope, and some members deliberately differ (e.g. `TransmitFile(path)` is a range- and cache-aware static file sender, and `WriteFile` is not provided).

Because `cshttp.web` writes responses through a 3-method interface (`IHttpResponseSink`), you can use `cshttp + cshttp.web` **without `cshttp.server`**—for example, to unit-test request handlers in memory at 100,000+ calls/sec or to run `System.Web`-style handlers over custom transports (named pipes, custom sockets, or serverless adapters).

---

## 2. File Inventory (`src/cshttp.web/` — 11 Files)

| File | Public Types | Role |
| :--- | :--- | :--- |
| **[HttpContext.cs](../src/cshttp.web/HttpContext.cs)** | `HttpContext`, `HttpApplication`, `HttpServerUtility` | Ambient `HttpContext.Current` (`AsyncLocal<HttpContext>`), per-request `Items`, `Server.MapPath`, and `CompleteRequest()`. |
| **[HttpRequest.cs](../src/cshttp.web/HttpRequest.cs)** | `HttpRequest`, `UnvalidatedRequestValues` | Lazy-parsed `Path`, `QueryString`, `Form`, `Files`, `Cookies`, `Headers`, `ServerVariables`, `Params`, and unified `Request["key"]` indexer. |
| **[HttpResponse.cs](../src/cshttp.web/HttpResponse.cs)** | `HttpResponse`, `IHttpResponseSink` | Buffered (`Content-Length`) or streaming (`Transfer-Encoding: chunked` via `Flush()` / `BufferOutput = false`) response writer and `TransmitFile`, which streams files from disk in 64 KB chunks at send time (never loading the whole file into memory). |
| **[CookiesAndFiles.cs](../src/cshttp.web/CookiesAndFiles.cs)** | `HttpCookie`, `HttpCookieCollection`, `SameSiteMode`, `HttpPostedFile`, `HttpFileCollection` | RFC 6265 cookies (with `HttpOnly`, `Secure`, `SameSite`) and zero-copy `HttpPostedFile` slices over the request body buffer. |
| **[MultipartParser.cs](../src/cshttp.web/MultipartParser.cs)** | `MultipartParser` | RFC 7578 `multipart/form-data` parser supporting multiple file uploads, RFC 8187 `filename*` UTF-8 decoding, and part-count limits. |
| **[UrlEncoding.cs](../src/cshttp.web/UrlEncoding.cs)** | `PercentDecoder`, `UrlEncodedParser` | Single-pass percent-decoder and `application/x-www-form-urlencoded` parser (operates directly on `byte[]` or `string`). |
| **[HttpUtility.cs](../src/cshttp.web/HttpUtility.cs)** | `HttpUtility` | `HtmlEncode`, `HtmlAttributeEncode`, `HtmlDecode`, `UrlEncode`, `UrlPathEncode`, `UrlDecode`, `ParseQueryString`, and `JavaScriptStringEncode`. |
| **[HttpCachePolicy.cs](../src/cshttp.web/HttpCachePolicy.cs)** | `HttpCachePolicy`, `HttpCacheability` | `Response.Cache` builder (`SetCacheability`, `SetMaxAge`, `SetNoStore`, `SetETag`, `SetLastModified`). |
| **[StaticFiles.cs](../src/cshttp.web/StaticFiles.cs)** | `StaticFiles`, `StaticFileOptions`, `MimeTypes` | Production static file server with `ETag`, `Last-Modified`, `304 Not Modified`, `206 Partial Content` byte ranges, `?v=` immutable cache-busting, and strict path-traversal guards. |
| **[HostingEnvironment.cs](../src/cshttp.web/HostingEnvironment.cs)** | `HostingEnvironment` | `ApplicationPhysicalPath`, `MapPath("~/...")` with root-escape protection, and graceful `QueueBackgroundWorkItem` tracking. |
| **[Common.cs](../src/cshttp.web/Common.cs)** | `HttpException`, `HttpResponseEndException`, `RequestSettings`, `RequestConnectionInfo`, `HttpValueCollection`, `HeaderValue` | Status-code exceptions, body-parsing limits (`MaxCollectionKeys = 1000`), and proxy-aware connection metadata. |

---

## 3. `HttpRequest`: Lazy Content Parsing & Ergonomics

When an `HttpRequest` is constructed from an `HttpRequestHead` and body `byte[]`, **nothing is parsed upfront**. Each collection is materialized only when your handler touches it:

```csharp
HttpRequest Request = HttpContext.Current.Request;

// 1. URL & Path (RFC 3986 percent-decoded)
string method = Request.HttpMethod;          // "POST"
string path   = Request.Path;                // "/files/my report.pdf"
string rawUrl = Request.RawUrl;              // "/files/my%20report.pdf?folder=docs"

// 2. Lazy-parsed collections
string folder = Request.QueryString["folder"]; // WHATWG / System.Web urlencoded query
string title  = Request.Form["title"];         // Parsed only if Content-Type is urlencoded or multipart
string sid    = Request.Cookies["ssid"]?.Value;

// 3. Unified System.Web Indexer (searches QueryString -> Form -> Cookies -> ServerVariables)
string action = Request["action"];

// 4. Zero-Copy Multipart File Uploads (RFC 7578)
HttpPostedFile upload = Request.Files["document"];
if (upload != null && upload.ContentLength > 0)
{
    string safeName = System.IO.Path.GetFileName(upload.FileName);
    upload.SaveAs(HostingEnvironment.MapPath("~/App_Data/uploads/" + safeName));
    // Or read directly from upload.InputStream without copying the underlying byte[] buffer!
}

// 5. Client IP & Proxy Resolution (populated after TrustedProxies validation)
string clientIp = Request.UserHostAddress;
bool isHttps    = Request.IsSecureConnection;
bool isLocal    = Request.IsLocal;
```

---

## 4. `HttpResponse`: Buffered, Streaming Chunked, and `TransmitFile`

`HttpResponse` automatically manages HTTP/1.1 framing so handlers never have to calculate `Content-Length` manually:

1. **Buffered Mode (Default — `BufferOutput = true`):**
   * `Response.Write(...)` / `Response.BinaryWrite(...)` data is queued in 16 KB memory segments; each `Response.TransmitFile(...)` call is queued as a single file segment (path + byte range) and read from disk only at send time.
   * If an exception is thrown before `Flush()`, the server can cleanly `Response.Reset()` and emit a `500` or `400` error page.
   * When the handler returns, `response.Complete()` sums the exact byte length of all memory segments and file ranges, emits `Content-Length: N`, and streams the response over the keep-alive connection.
2. **Streaming Chunked Mode (`Response.Flush()` or `BufferOutput = false`):**
   * Calling `Response.Flush()` mid-handler (or setting `Response.BufferOutput = false`) immediately sends the HTTP headers with `Transfer-Encoding: chunked` and wraps each subsequent write in RFC 9112 chunk framing (`{hex}\r\n...\r\n`), finishing with `0\r\n\r\n` when the handler completes.
3. **Safe `Response.End()` without `ThreadAbortException`:**
   * In classic IIS, `Response.End()` throws `ThreadAbortException`, which is expensive and uncatchable. In `cshttp.web`, `Response.End()` flushes the response, marks `_ended = true` (ignoring any subsequent writes even if a user `catch (Exception)` block swallows the exception), and throws lightweight `HttpResponseEndException`.

### `Response.TransmitFile`: Two Overloads, Two Jobs

| Overload | Behavior |
| :--- | :--- |
| `bool TransmitFile(string path)` | **Smart file sender** for a file *your code* chose. Sets `Content-Type` from the extension, emits `ETag` / `Last-Modified` / `Accept-Ranges`, answers `304`, and supports resumable downloads (`Range` / `If-Range` → `206` / `416`). Only `GET` / `HEAD` are allowed (others get `405`). Returns `false` (sending nothing) if the file is missing, hidden/system, or has an unknown extension. Cache-Control is `public, max-age=3600`, or `public, max-age=31536000, immutable` when the URL has `?v=`. |
| `void TransmitFile(string path, long offset, long length)` | **Raw sender**: queues exactly that byte range (`length = -1` means to end of file); no headers, no conditional or range logic. Throws `FileNotFoundException` if missing. |

If the handler has already set `ContentType` explicitly (or already written body data), `TransmitFile(path)` skips the smart logic and sends the whole file raw — use this for dynamic downloads such as generated reports, where public caching and the `405` check are not wanted.

Files are streamed from disk in 64 KB chunks at send time; the whole file is never loaded into memory. `WriteFile` is **not** provided — use `TransmitFile`.

---

## 5. `StaticFiles`: Production Static Asset Serving with Range & Cache Support

`StaticFiles.TryServe` is the **folder-serving route** (URL → safe file path → policy), whereas `Response.TransmitFile` sends a file your code already chose. Use `TryServe` whenever the file path comes from the request URL. When running standalone without Nginx (or in development/LAN deployments), `StaticFiles.TryServe` serves static folders with full HTTP semantics and hardened security:

```csharp
// Simple one-liner inside your request handler:
if (StaticFiles.TryServe(ctx, "/css/", "css")) return;
if (StaticFiles.TryServe(ctx, "/js/", "js")) return;

// Or with custom StaticFileOptions:
var opts = new StaticFileOptions("/assets/", "wwwroot/assets")
{
    MaxAge = TimeSpan.FromHours(1),
    ImmutableWhenVersioned = true, // ?v=20261020 -> "public, max-age=31536000, immutable"
    VersionQueryKey = "v",
    UseValidators = true,          // Emits ETag & Last-Modified; answers 304 Not Modified
    ServeUnknownFileTypes = false  // Blocks .config, .cs, .db, etc. (404 like IIS)
};
if (StaticFiles.TryServe(ctx, opts)) return;
```

**Built-in `StaticFiles` Security & Protocol Features:**
* **Path Traversal Hardening:** Rejects `..`, backslashes (`\`), drive colons (`:`), NUL bytes (`%00`), control characters, trailing dots/spaces (which Windows NTFS silently strips), and hidden/system files. Verified against 13 path-traversal attack vectors in `ServerTests.cs`.
* **Byte-Range Streaming (`206 Partial Content` / `416 Range Not Satisfiable`):** Supports video/audio scrubbing and resumable downloads (`Range: bytes=0-1023`, `If-Range`), streaming directly from disk in 64 KB chunks via `TransmitFile`.
* **Per-Folder Policy:** Unlike `TransmitFile(path)`, `TryServe` honors `StaticFileOptions` (`MaxAge`, `MaxAgeOverride`, `VersionQueryKey`, `UseValidators`, `ServeUnknownFileTypes` / `DefaultContentType`, `PassThroughWhenMissing`). Missing files return `404` (or `false` with `PassThroughWhenMissing`), and requests outside the prefix return `false` so routing can continue.

---

## 6. Using `cshttp.web` Standalone (In-Memory Handler Testing Without a Socket)

Because `HttpResponse` writes to `IHttpResponseSink`, you can invoke any `cshttp.web` handler in-memory without opening a TCP port:

```csharp
using System;
using System.IO;
using System.Text;
using cshttp;

sealed class MemorySink : IHttpResponseSink
{
    public readonly MemoryStream Stream = new MemoryStream();
    public void Write(byte[] buffer, int offset, int count) => Stream.Write(buffer, offset, count);
    public void Flush() { }
    public bool IsConnected => true;
}

// Construct a synthetic request and run your handler directly in memory:
byte[] raw = Encoding.ASCII.GetBytes("GET /echo?name=Adrian HTTP/1.1\r\nHost: localhost\r\n\r\n");
HttpRequestHead head;
HttpRequestParser.ParseHead(raw, 0, raw.Length, null, out head);

var sink = new MemorySink();
var ctx = new HttpContext(
    new HttpRequest(head, null, 0, 0, null, null),
    new HttpResponse(sink, head));

HttpContext.Current = ctx;
try
{
    ctx.Response.Write("Hello " + ctx.Request.QueryString["name"]);
    ctx.Response.Complete();
}
finally
{
    HttpContext.Current = null;
}

string wireOutput = Encoding.UTF8.GetString(sink.Stream.ToArray());
```
