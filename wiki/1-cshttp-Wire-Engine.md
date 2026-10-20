# Tier 1 Deep Dive: `cshttp` — The Pure Wire Engine

> **Source Folder:** [`src/cshttp/`](../src/cshttp/)  
> **Namespace:** `cshttp`  
> **Dependencies:** **Zero** (Pure BCL, C# 7.3, compiles on `.NET Framework 4.8` and `.NET 8 / 10+`)

---

## 1. Overview & Design Philosophy

**`cshttp`** is the lowest tier of the stack: a strict, resumable, byte-level **HTTP/1.1 wire parser, chunked transfer decoder, and response head serializer** built against **RFC 9112** and **RFC 9110**.

It performs **zero network I/O**, knows nothing about sockets or streams (except a convenience chunk helper), and uses **zero regular expressions** on untrusted input (making it 100% immune to ReDoS attacks).

Use `cshttp` on its own whenever you already own the byte buffer—such as inside a custom TCP/TLS proxy, packet analyzer, load balancer, embedded socket loop, or unit test harness—and want to turn `byte[]` slices into structured HTTP headers and back without allocating until a complete message head has arrived.

---

## 2. File Inventory (`src/cshttp/` — 7 Files)

| File | Public Types | Role |
| :--- | :--- | :--- |
| **[HttpRequestParser.cs](../src/cshttp/HttpRequestParser.cs)** | `HttpRequestParser` | Strict, resumable HTTP/1.x request-head parser (`ParseHead`) and comma-list splitter (`SplitList`). |
| **[HttpRequestHead.cs](../src/cshttp/HttpRequestHead.cs)** | `HttpRequestHead`, `HttpParseResult`, `HttpParseStatus`, `HttpBodyKind`, `RequestTargetForm` | Parsed request-line, headers, body framing decision (`None`, `ContentLength`, `Chunked`), keep-alive flag, and `100-continue` expectation. |
| **[HttpHeaders.cs](../src/cshttp/HttpHeaders.cs)** | `HttpHeaders`, `HttpHeader`, `KnownHeaders` | Wire-order-preserving header list with case-insensitive lookup and zero-allocation canonical name interning (`KnownHeaders.Find`). |
| **[ChunkedDecoder.cs](../src/cshttp/ChunkedDecoder.cs)** | `ChunkedDecoder`, `ChunkedStatus` | Incremental, zero-copy state machine for `Transfer-Encoding: chunked` (RFC 9112 §7.1). |
| **[HttpResponseWriter.cs](../src/cshttp/HttpResponseWriter.cs)** | `HttpResponseWriter`, `HttpStatus`, `HttpDate` | Serializes response status lines and headers (`BuildHead`) with CR/LF injection protection, chunk framing (`WriteChunkHeader`), and IMF-fixdate formatting. |
| **[HttpChars.cs](../src/cshttp/HttpChars.cs)** | `HttpChars`, `ParserOptions` | RFC 9110/9112 byte character-class lookup tables (`IsToken`, `IsFieldValueByte`, `IsSafeFieldValue`) and parser size/strictness limits. |
| **[ByteScan.cs](../src/cshttp/ByteScan.cs)** | `ByteScan` | Runtime-adaptive byte scanning: uses SIMD vectorized `ReadOnlySpan<byte>.IndexOf` on `.NET 8+` and `Array.IndexOf` on `.NET Framework 4.8` without requiring `System.Memory`. |

---

## 3. Resumable Request-Head Parsing (`HttpRequestParser.ParseHead`)

Unlike naive parsers that block on `StreamReader.ReadLine()` or allocate strings on partial packets, `HttpRequestParser.ParseHead` is **resumable and allocation-free on incomplete data**:

* You pass the accumulated buffer (`buffer`, `offset`, `count`) from the same start offset each time new bytes arrive from the socket.
* Until the blank line (`\r\n\r\n`) terminating the HTTP head is present, `ParseHead` scans only for linefeeds and limits, allocates **nothing on the managed heap**, and returns `HttpParseStatus.NeedMoreData`.
* Once the blank line arrives, it validates the request-line, headers, and RFC 9112 framing rules in a single pass and returns `HttpParseStatus.Complete` along with `Consumed` (the exact byte offset where the request body—or the next pipelined request—begins).
* If the client violates RFC 9112, `ParseHead` immediately returns `HttpParseStatus.Error` with the exact HTTP status code (`400`, `414`, `417`, `431`, `501`, `505`) the server should reply with before closing the socket.

### Code Example: Parsing a Request Head from a Socket Buffer

```csharp
using System;
using cshttp;

// Suppose 'buf' holds bytes read from a socket from index 'start' to 'end'
var options = new ParserOptions
{
    MaxRequestLineLength = 8 * 1024, // 414 URI Too Long if exceeded
    MaxHeaderBytes = 32 * 1024,      // 431 Request Header Fields Too Large if exceeded
    MaxHeaderCount = 100,            // 431 if exceeded
    AllowBareLf = false              // Strict CRLF required by default
};

HttpRequestHead head;
HttpParseResult result = HttpRequestParser.ParseHead(buf, start, end - start, options, out head);

switch (result.Status)
{
    case HttpParseStatus.NeedMoreData:
        // Read more bytes into 'buf' at 'end' and call ParseHead again with the same 'start'
        break;

    case HttpParseStatus.Error:
        // Send result.ErrorStatusCode (e.g. 400, 414, 431, 505) and close the connection
        Console.WriteLine($"HTTP {result.ErrorStatusCode}: {result.ErrorMessage}");
        break;

    case HttpParseStatus.Complete:
        start += result.Consumed; // Advance past the HTTP head to the body (or next pipelined request)

        Console.WriteLine($"Method     : {head.Method}");       // e.g. "POST" (interned for common methods)
        Console.WriteLine($"Path       : {head.Path}");         // Raw path, e.g. "/api/upload"
        Console.WriteLine($"Query      : {head.Query}");        // Raw query without '?', or null
        Console.WriteLine($"Version    : {head.Version}");      // "HTTP/1.1" or "HTTP/1.0"
        Console.WriteLine($"Host       : {head.Host}");         // Validated Host header or absolute-URI authority
        Console.WriteLine($"KeepAlive  : {head.KeepAlive}");    // RFC 9112 Connection keep-alive resolution
        Console.WriteLine($"BodyKind   : {head.BodyKind}");     // None, ContentLength, or Chunked
        Console.WriteLine($"ContentLen : {head.ContentLength}");// Declared bytes, 0, or -1 for Chunked
        break;
}
```

---

## 4. Built-In RFC 9112 Request-Smuggling & Security Defenses

`HttpRequestParser` enforces every RFC 9112 security requirement at the byte level:

| Attack Vector | `cshttp` Defense | Error Status |
| :--- | :--- | :---: |
| **TE.CL / CL.TE Request Smuggling** | Rejects any request containing both `Transfer-Encoding` and `Content-Length` (`RFC 9112 §6.3`). | `400 Bad Request` |
| **Duplicate / Conflicting `Content-Length`** | Rejects multiple `Content-Length` headers or comma-separated lists with differing values (e.g. `Content-Length: 5, 10`). | `400 Bad Request` |
| **Transfer-Encoding Obfuscation** | Rejects `Transfer-Encoding` in HTTP/1.0; requires the final coding to be `chunked`; rejects `chunked` applied more than once; rejects unknown transfer codings. | `400` / `501` |
| **Bare CR / Bare LF Injection** | Rejects bare `\r` anywhere in the start-line or headers; rejects bare `\n` unless `ParserOptions.AllowBareLf = true`. | `400 Bad Request` |
| **Obsolete Line Folding (`obs-fold`)** | Rejects header continuation lines starting with `SP` or `HTAB` (`RFC 9112 §5.2`). | `400 Bad Request` |
| **Whitespace Before Colon** | Rejects `Header-Name : value` (`RFC 9112 §5.1` smuggling vector). | `400 Bad Request` |
| **Host Header Spoofing** | Requires exactly one valid `Host` header in HTTP/1.1; rejects multiple `Host` headers or invalid authority characters. | `400 Bad Request` |
| **Oversized Request Line / Headers** | Enforces `MaxRequestLineLength` (default 8 KB) and `MaxHeaderBytes` (default 32 KB) before allocating strings. | `414` / `431` |

---

## 5. Incremental Zero-Copy Chunked Decoding (`ChunkedDecoder`)

When `head.BodyKind == HttpBodyKind.Chunked`, use `ChunkedDecoder` to decode the body stream incrementally without copying bytes out of your receive buffer:

```csharp
var decoder = new ChunkedDecoder
{
    MaxLineLength = 4096,
    MaxTrailerBytes = 16 * 1024,
    AllowBareLf = false
};

while (true)
{
    int consumed, dataOffset, dataCount;
    ChunkedStatus status = decoder.Decode(buf, start, end - start, out consumed, out dataOffset, out dataCount);
    start += consumed;

    if (status == ChunkedStatus.Data)
    {
        // Zero-copy slice inside 'buf' at [dataOffset .. dataOffset + dataCount]
        outputStream.Write(buf, dataOffset, dataCount);
        continue;
    }
    if (status == ChunkedStatus.Complete)
    {
        // Terminating '0\r\n\r\n' chunk and trailers consumed!
        // Any remaining bytes in 'buf' after 'start' belong to the next pipelined request.
        break;
    }
    if (status == ChunkedStatus.Error)
    {
        throw new Exception("Malformed chunked body: " + decoder.ErrorMessage);
    }
    if (status == ChunkedStatus.NeedMoreData)
    {
        // Read more bytes from the socket into 'buf'
    }
}
```

---

## 6. Response Head Serialization & Header Injection Guard (`HttpResponseWriter`)

`HttpResponseWriter` builds wire-ready HTTP/1.1 response headers while validating every header name (`HttpChars.IsToken`) and value (`HttpChars.IsSafeFieldValue`) to guarantee that untrusted user data can never inject `\r` or `\n` (HTTP Response Splitting):

```csharp
var headers = new HttpHeaders();
headers.Add("Date", HttpDate.Format(DateTime.UtcNow));
headers.Add("Content-Type", "application/json; charset=utf-8");
headers.Add("Content-Length", "15");
headers.Add("Connection", "keep-alive");

// Returns Latin-1 byte[] for "HTTP/1.1 200 OK\r\nDate: ...\r\n\r\n"
// Throws ArgumentException if any header name or value contains CR/LF/NUL control chars.
byte[] headBytes = HttpResponseWriter.BuildHead(200, null, headers);
```
