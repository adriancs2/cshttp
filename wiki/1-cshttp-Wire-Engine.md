# Tier 1 Complete Reference: `cshttp.core` — The RFC 9112 Wire Engine

> **Source Location:** [`src/cshttp/Core/`](../src/cshttp/Core/)  
> **Namespace:** `cshttp`  
> **Compilation:** Pure C# 7.3, zero external dependencies, compiles on `.NET Framework 4.8` and `.NET 8 / 10+`.

---

## 1. Overview & Architecture

`cshttp.core` is the foundational tier of `cshttp`: a strict, resumable, byte-level **HTTP/1.1 wire parser, chunked transfer decoder, and response serializer** built directly against authoritative internet standards:
* [RFC 9112 — HTTP/1.1](https://www.rfc-editor.org/rfc/rfc9112)
* [RFC 9110 — HTTP Semantics](https://www.rfc-editor.org/rfc/rfc9110)
* [RFC 3986 — Uniform Resource Identifier (URI)](https://www.rfc-editor.org/rfc/rfc3986)
* [RFC 6265 — HTTP State Management Mechanism (Cookies)](https://www.rfc-editor.org/rfc/rfc6265)
* [RFC 7578 — Returning Values from Forms: multipart/form-data](https://www.rfc-editor.org/rfc/rfc7578)

### Design Principles
1. **Zero Socket / Network I/O**: The engine is purely computational. It knows nothing about sockets, listeners, SSL, or files. You pass it incoming `byte[]` chunks; it validates, inspects, and yields structured message heads and bodies.
2. **Resumable & Allocation-Free on Incomplete Data**: Unlike naive parsers that call `StreamReader.ReadLine()` or allocate strings on partial TCP packets, `HttpRequestParser.ParseHead` scans strictly for delimiter bounds. Until the terminating double CRLF (`\r\n\r\n`) arrives, it allocates **zero objects on the managed heap** and returns `HttpParseStatus.NeedMoreData`.
3. **Zero Regular Expressions (ReDoS Immune)**: Untrusted client byte streams are validated with RFC character-class lookup tables (`HttpChars`), completely eliminating Regular Expression Denial of Service (ReDoS) vectors.
4. **Active Request-Smuggling Defense**: Enforces RFC 9112 §6.3, rejecting TE+CL conflicts, duplicate/differing `Content-Length` headers, bare CR injection, and obsolete line folding (`obs-fold`).

---

## 2. File Inventory (`src/cshttp/Core/` — 7 Source Files)

| Source File | Public Types | Architectural Role |
| :--- | :--- | :--- |
| **`HttpRequestParser.cs`** | `HttpRequestParser` | Strict, resumable HTTP/1.x request-head parser (`ParseHead`) and RFC comma-list splitter (`SplitList`). |
| **`HttpRequestHead.cs`** | `HttpRequestHead`<br>`HttpParseResult`<br>`HttpParseStatus`<br>`HttpBodyKind`<br>`RequestTargetForm` | Parsed request-line, headers, body framing resolution (`None`, `ContentLength`, `Chunked`), keep-alive decision, and `100-continue` flag. |
| **`HttpHeaders.cs`** | `HttpHeaders`<br>`HttpHeader`<br>`KnownHeaders` | Wire-order-preserving header list with case-insensitive lookup and zero-allocation canonical name interning (`KnownHeaders.Find`). |
| **`ChunkedDecoder.cs`** | `ChunkedDecoder`<br>`ChunkedStatus` | Incremental, zero-copy finite state machine for `Transfer-Encoding: chunked` (RFC 9112 §7.1). |
| **`HttpResponseWriter.cs`** | `HttpResponseWriter`<br>`HttpStatus`<br>`HttpDate` | Serializes HTTP/1.1 response status lines, headers, chunk framing, and IMF-fixdate formatting with CRLF injection protection. |
| **`HttpChars.cs`** | `HttpChars`<br>`ParserOptions` | RFC 9110/9112 byte character-class lookup tables (`IsToken`, `IsFieldValueByte`, `IsSafeFieldValue`) and parser safety limits. |
| **`ByteScan.cs`** | `ByteScan` | SIMD vectorized scanning: uses `ReadOnlySpan<byte>.IndexOf` on modern `.NET 8+` and fast loops on `.NET Framework 4.8` without external packages. |

---

## 3. Resumable Request-Head Parsing (`HttpRequestParser.ParseHead`)

The primary entry point for parsing HTTP wire traffic is `HttpRequestParser.ParseHead`:

```csharp
public static HttpParseResult ParseHead(
    byte[] buffer,
    int offset,
    int count,
    ParserOptions options,
    out HttpRequestHead head)
```

### Execution Flow
1. **Accumulate**: You pass the slice of your buffer (`buffer, offset, count`) representing bytes received from the network so far.
2. **Scan**: `ParseHead` verifies if the header terminator `\r\n\r\n` (or `\n\n` in lenient mode) exists.
3. **Evaluate**:
   * If not found: Returns `HttpParseStatus.NeedMoreData`. You wait for the socket to read more bytes and call `ParseHead` again with the updated count.
   * If found: Parses the start-line, decodes the request method and URI target, interns known headers, resolves body framing, and returns `HttpParseStatus.Complete`.
   * If malformed: Returns `HttpParseStatus.Error` with the exact HTTP error status code (`400`, `414`, `431`, `501`, `505`).

### Code Example: Socket Buffer Parsing
```csharp
using System;
using cshttp;

var options = new ParserOptions
{
    MaxRequestLineLength = 8 * 1024, // 414 URI Too Long if exceeded
    MaxHeaderBytes = 32 * 1024,      // 431 Request Header Fields Too Large
    MaxHeaderCount = 100,            // 431 if exceeded
    AllowBareLf = false              // Strict RFC CRLF enforcement
};

HttpRequestHead head;
HttpParseResult result = HttpRequestParser.ParseHead(buf, start, end - start, options, out head);

switch (result.Status)
{
    case HttpParseStatus.NeedMoreData:
        // Keep buffer intact, read more bytes into 'buf' at 'end', call ParseHead again
        break;

    case HttpParseStatus.Error:
        // Reply with result.ErrorStatusCode (400, 414, 431, 505) and close socket
        Console.WriteLine($"[HTTP {result.ErrorStatusCode}] {result.ErrorMessage}");
        break;

    case HttpParseStatus.Complete:
        // Advance buffer pointer past the head:
        start += result.Consumed;

        Console.WriteLine($"Method        : {head.Method}");       // e.g. "POST"
        Console.WriteLine($"Path          : {head.Path}");         // Raw path, e.g. "/api/upload"
        Console.WriteLine($"Query         : {head.Query}");        // Raw query string or null
        Console.WriteLine($"Version       : {head.Version}");      // "HTTP/1.1"
        Console.WriteLine($"Host          : {head.Host}");         // Canonical Host header
        Console.WriteLine($"KeepAlive     : {head.KeepAlive}");    // Resolved connection reuse
        Console.WriteLine($"BodyKind      : {head.BodyKind}");     // None | ContentLength | Chunked
        Console.WriteLine($"ContentLength : {head.ContentLength}");// Length in bytes (-1 for chunked)
        break;
}
```

---

## 4. Message Models & Structures

### `HttpRequestHead`
Represents the fully validated HTTP request envelope:

| Property | Type | Description |
| :--- | :--- | :--- |
| `Method` | `string` | HTTP method verb (`"GET"`, `"POST"`, `"PUT"`, `"DELETE"`, etc.). Common methods are interned. |
| `Target` | `string` | The exact request target as received on the wire (`"/index.html?ref=1"`). |
| `TargetForm` | `RequestTargetForm` | RFC 9112 form: `Origin` (`/path`), `Absolute` (`http://...`), `Authority` (`host:port`), or `Asterisk` (`*`). |
| `Path` | `string` | Extracted URI path component. |
| `Query` | `string` | Extracted raw query string (without leading `?`), or `null` if absent. |
| `Version` | `string` | HTTP version (`"HTTP/1.1"` or `"HTTP/1.0"`). |
| `Headers` | `HttpHeaders` | The wire-ordered header collection. |
| `Host` | `string` | Validated Host header authority. |
| `KeepAlive` | `bool` | RFC 9112 connection lifetime resolution (`true` for keep-alive, `false` for close). |
| `Expect100Continue`| `bool` | `true` if `Expect: 100-continue` header was sent by the client. |
| `BodyKind` | `HttpBodyKind` | `None`, `ContentLength`, or `Chunked`. |
| `ContentLength` | `long` | Declared body length in bytes (`0` for `None`, `-1` for `Chunked`). |

### `HttpHeaders` Collection
`HttpHeaders` preserves strict wire order while enabling case-insensitive $\mathcal{O}(1)$ lookups:

* **`headers["Host"]`**: Returns the value of the header, or `null` if not found. If multiple occurrences exist, returns a comma-delimited combination per RFC 9110 §5.3.
* **`headers.GetValues("Header-Name")`**: Returns an array of all values for the specified name without comma-merging (mandatory for multiple `Set-Cookie` headers).
* **`headers.Contains("Header-Name")`**: Fast boolean check.
* **`headers.Add(name, value)`**: Appends a header while checking for forbidden characters.

---

## 5. Body Framing Rules (RFC 9112 §6.3)

`cshttp.core` strictly implements the 8 precedence rules defined in **RFC 9112 Section 6.3**:

| Precedence | Condition | Resulting `HttpBodyKind` | Action |
| :---: | :--- | :---: | :--- |
| **Rule 1** | 1xx, 204, or 304 response | `None` | Any `Content-Length` or `Transfer-Encoding` is ignored or rejected. |
| **Rule 2** | 2xx response to `CONNECT` | `Tunnel` | Direct socket tunnel established. |
| **Rule 3** | `Transfer-Encoding` + `Content-Length` | `Error (400)` | **Request Smuggling Guard**: Any message containing both framing headers is rejected immediately. |
| **Rule 4** | `Transfer-Encoding: chunked` | `Chunked` | Body is framed using chunked transfer coding; final chunk `0\r\n\r\n` concludes the stream. |
| **Rule 5** | Duplicate `Content-Length` | `ContentLength` or `Error (400)` | Multiple identical values (e.g. `10, 10`) are collapsed; conflicting values (e.g. `10, 20`) trigger `400 Bad Request`. |
| **Rule 6** | Valid single `Content-Length` | `ContentLength` | Exactly `Content-Length` bytes form the message body. |
| **Rule 7** | Request with neither TE nor CL | `None` | The request has no message body (`ContentLength = 0`). |
| **Rule 8** | Response with neither TE nor CL | `UntilClose` | Response body continues until the server closes the TCP socket. |

---

## 6. Incremental Zero-Copy `ChunkedDecoder`

When `head.BodyKind == HttpBodyKind.Chunked`, `ChunkedDecoder` parses chunk boundaries, hex sizes, chunk extensions, and trailers incrementally without intermediate byte copying:

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
        // Zero-copy slice directly inside the existing 'buf':
        outputStream.Write(buf, dataOffset, dataCount);
        continue;
    }
    if (status == ChunkedStatus.Complete)
    {
        // Terminal chunk '0\r\n\r\n' and all trailers have been fully parsed!
        // start now points to the next pipelined HTTP request in the buffer.
        break;
    }
    if (status == ChunkedStatus.NeedMoreData)
    {
        // Read more bytes from network into 'buf'
        break;
    }
    if (status == ChunkedStatus.Error)
    {
        throw new InvalidOperationException("Chunked decode error: " + decoder.ErrorMessage);
    }
}
```

---

## 7. Response Serialization (`HttpResponseWriter`)

`HttpResponseWriter` constructs wire-ready byte arrays for outbound HTTP responses. It enforces strict header validation to protect against **HTTP Response Splitting**:

```csharp
var headers = new HttpHeaders();
headers.Add("Date", HttpDate.Format(DateTime.UtcNow));
headers.Add("Content-Type", "text/html; charset=utf-8");
headers.Add("Content-Length", bodyBytes.Length.ToString());
headers.Add("Connection", "keep-alive");

// Serializes the status line and headers into a wire-ready ASCII/Latin-1 byte[]
// Throws ArgumentException if any header key or value contains raw \r, \n, or \0 characters.
byte[] headBytes = HttpResponseWriter.BuildHead(200, "OK", headers);
```

### Chunked Response Helpers
When streaming dynamic output where total length is unknown:
* `HttpResponseWriter.WriteChunkHeader(long size)`: Returns the chunk size in hex followed by CRLF (`e.g. "1A4\r\n"`).
* `HttpResponseWriter.FinalChunk`: Static byte array containing the terminal chunk (`"0\r\n\r\n"`).

---

## 8. Security Specifications & Defenses

| Attack Vector | RFC Reference | `cshttp.core` Enforcement | Error Code |
| :--- | :--- | :--- | :---: |
| **TE.CL / CL.TE Smuggling** | RFC 9112 §6.3 | Messages with both `Transfer-Encoding` and `Content-Length` are rejected immediately. | `400 Bad Request` |
| **Duplicate `Content-Length`** | RFC 9112 §6.3 | Conflicting `Content-Length` values are rejected. Identical duplicates are safely collapsed. | `400 Bad Request` |
| **Whitespace Before Colon** | RFC 9112 §5.1 | `Header-Name : value` is rejected as an invalid smuggling probe. | `400 Bad Request` |
| **Obsolete Line Folding (`obs-fold`)** | RFC 9112 §5.2 | Header continuations starting with SP/HTAB are rejected by default. | `400 Bad Request` |
| **Bare CR / LF Injection** | RFC 9112 §2.2 | Bare `\r` is rejected everywhere. Bare `\n` is rejected unless `AllowBareLf = true`. | `400 Bad Request` |
| **Header Injection / Response Splitting** | RFC 9110 | Header keys and values must pass `HttpChars.IsToken` and `HttpChars.IsSafeFieldValue`. | `ArgumentException` |
| **URI & Header Flooding** | RFC 9112 | Strict bounds: `MaxRequestLineLength` (8 KB) and `MaxHeaderBytes` (32 KB) enforced before allocations. | `414` / `431` |
| **ReDoS (Regex Denial of Service)** | — | **Zero regular expressions** used on untrusted byte input. | Immune |

---

## 9. Configuration (`ParserOptions`)

```csharp
public sealed class ParserOptions
{
    public int MaxRequestLineLength { get; set; } = 8192;   // 8 KB
    public int MaxHeaderBytes { get; set; } = 32768;        // 32 KB
    public int MaxHeaderCount { get; set; } = 100;          // Max number of header lines
    public bool AllowBareLf { get; set; } = false;          // false = Strict CRLF required
    public bool RejectObsFold { get; set; } = true;         // Reject obsolete line folding
}
```

---

## 10. Specification & RFC Reference Links

| Standard | Title & Link | Scope in `cshttp.core` |
| :--- | :--- | :--- |
| **RFC 9112** | [HTTP/1.1 (Message Syntax and Routing)](https://www.rfc-editor.org/rfc/rfc9112) | Start-line grammar, header field lines, chunked transfer coding, request smuggling defenses (TE+CL), and body framing rules (§6.3). |
| **RFC 9110** | [HTTP Semantics](https://www.rfc-editor.org/rfc/rfc9110) | HTTP methods, status codes, reason phrases, character tokens (`HttpChars`), and IMF-fixdate format (`HttpDate`). |
| **RFC 3986** | [Uniform Resource Identifier (URI): Generic Syntax](https://www.rfc-editor.org/rfc/rfc3986) | Request-target forms (Origin, Absolute, Authority, Asterisk), path and query string delimiters, percent-decoding rules. |
| **RFC 6265** | [HTTP State Management Mechanism (Cookies)](https://www.rfc-editor.org/rfc/rfc6265) | `Cookie` and `Set-Cookie` header serialization and token constraints. |
| **RFC 7578** | [Returning Values from Forms: multipart/form-data](https://www.rfc-editor.org/rfc/rfc7578) | Boundary syntax, multi-part headers, and form-data framing. |
| **WHATWG URL** | [WHATWG URL Standard](https://url.spec.whatwg.org/) | Query parameter parsing, `application/x-www-form-urlencoded`, and `+` to space mapping. |

---

## 11. Summary: When to Use `cshttp.core`

* **Use `cshttp.core` directly** if you are writing custom network infrastructure (TCP reverse proxies, TLS terminators, API gateways, network packet inspectors, or custom socket loops) and only require high-performance, specification-compliant HTTP/1.1 byte framing without web abstractions.
* **For general web application development**, use the unified [`cshttp`](../README.md) micro web server, which bundles `cshttp.core`, the `cshttp.web` toolkit (`HttpRequest`, `HttpResponse`, `Session`, `Cookies`, `StaticFiles`), and the `HttpServer` socket listener into one complete stack.
