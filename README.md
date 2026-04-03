# cshttp

**A standalone HTTP/1.1 request and response parser for C#. No framework. No server. No dependencies. Just bytes in, structured message out.**

cshttp parses raw HTTP/1.1 bytes into structured objects. You give it a `byte[]` containing an HTTP request or response. It gives you back a clean object with the method, path, headers, body, and all the structural details — fully parsed, validated, and ready to use. The parser implements [RFC 9112](https://www.rfc-editor.org/rfc/rfc9112) (HTTP/1.1 Message Syntax). Every parsing decision maps to a specific section of the specification.

cshttp has zero external dependencies. It targets C# 7.3 and .NET Framework 4.8. The entire parser is contained in seven source files that can be dropped into any project.

## Quick Start

Parse an HTTP request:

```csharp
using CsHttp;
using System.Text;

byte[] data = Encoding.ASCII.GetBytes(
    "POST /submit HTTP/1.1\r\n" +
    "Host: example.com\r\n" +
    "Content-Length: 11\r\n" +
    "\r\n" +
    "Hello World");

ParseResult result = CsHttpParser.ParseRequest(data);

if (result.Success)
{
    HttpRequestMessage req = result.Request;

    string method  = req.Method;            // "POST"
    string target  = req.RequestTarget;     // "/submit"
    string version = req.Version;           // "HTTP/1.1"
    string host    = req.Headers["Host"];   // "example.com"
    string body    = Encoding.UTF8.GetString(req.Body); // "Hello World"
}
else
{
    Console.WriteLine(result.Error); // [InvalidMethod] at byte 0: ...
}
```

Parse an HTTP response:

```csharp
byte[] data = Encoding.ASCII.GetBytes(
    "HTTP/1.1 200 OK\r\n" +
    "Content-Length: 13\r\n" +
    "\r\n" +
    "Hello, World!");

ParseResult result = CsHttpParser.ParseResponse(data, "GET");

if (result.Success)
{
    int statusCode = result.Response.StatusCode;    // 200
    string reason  = result.Response.ReasonPhrase;  // "OK"
    string body    = Encoding.UTF8.GetString(result.Response.Body);
}
```

## Why cshttp Exists

Today, a C# developer who needs to parse HTTP has two choices: adopt the entire ASP.NET Core framework (Kestrel, middleware pipeline, hosting abstractions, dependency injection), or write a fragile hand-rolled parser that handles the happy path and silently breaks on everything else.

There is no middle ground. No standalone, specification-complete HTTP parser exists as a reusable component in the .NET ecosystem.

Every other major language resolved this years ago. C has `llhttp` (the parser inside Node.js). Rust has `httparse`. Python has `httptools`. These are standalone libraries — bytes in, structure out, no framework attached. The .NET ecosystem bundled the parser inside the server and never separated it.

cshttp fills that gap. It separates the parser from the server and makes it available as an independent tool.

## Features

- **RFC 9112 compliant** — request-line, status-line, headers, all 8 body framing rules, chunked transfer encoding with trailers, all implemented against the specification
- **Request smuggling protection** — conflicting `Transfer-Encoding` and `Content-Length` headers detected and rejected (configurable)
- **Strict and lenient modes** — strict rejects all deviations; lenient tolerates what the RFC permits and records every deviation as a warning
- **Configurable limits and callbacks** — max header count, max body size, max chunk size, plus four callback hooks for early rejection and progress monitoring
- **Pipeline support** — `BytesConsumed` tracks exactly how many bytes were consumed, enabling parsing of pipelined requests from a single buffer
- **Zero dependencies** — no NuGet packages, no framework references beyond the base class library; targets C# 7.3 / .NET Framework 4.8

## Installation

cshttp is distributed as source files. Copy the seven files from the `src/` directory into your project:

| File | Description |
|------|-------------|
| `CsHttpParser.cs` | Core parser — start-line, header, and body parsing logic |
| `HttpMessage.cs` | Message models — `HttpRequestMessage`, `HttpResponseMessage`, enums |
| `HttpHeaderCollection.cs` | Wire-order header collection with case-insensitive access |
| `ParserOptions.cs` | Configuration options and callback delegate definitions |
| `ParseResult.cs` | Parse result container with success/failure/warnings |
| `ParseError.cs` | Error kinds and error model |
| `ParseWarning.cs` | Warning kinds and warning model |

All files use the `CsHttp` namespace. No NuGet package installation required.

**Requirements:** C# 7.3 or later. .NET Framework 4.8, .NET 6.0, or any compatible runtime.

**Build and test:**

```bash
# .NET SDK (cross-platform)
dotnet build
dotnet run

# .NET Framework (Windows, command line)
csc -langversion:7.3 -target:exe -out:CsHttp.Tests.exe src\*.cs tests\*.cs
CsHttp.Tests.exe
```

## API Overview

### CsHttpParser

The static parser class. Two entry points:

| Method | Description |
|--------|-------------|
| `CsHttpParser.ParseRequest(byte[] data, ParserOptions options)` | Parses an HTTP request message from raw bytes |
| `CsHttpParser.ParseResponse(byte[] data, string requestMethod, ParserOptions options)` | Parses an HTTP response message from raw bytes |

Both methods also accept `(byte[] data, int offset, int length, ...)` overloads for parsing from a region of a larger buffer — essential for pipelined messages.

### ParseResult

The result of a parse operation.

| Property | Type | Description |
|----------|------|-------------|
| `Success` | `bool` | Whether parsing completed successfully |
| `Error` | `ParseError` | The error that caused failure (null on success) |
| `Request` | `HttpRequestMessage` | The parsed request (null if response or failure) |
| `Response` | `HttpResponseMessage` | The parsed response (null if request or failure) |
| `Message` | `HttpMessage` | The parsed message regardless of type |
| `Warnings` | `IReadOnlyList<ParseWarning>` | Tolerated deviations recorded during parsing |
| `BytesConsumed` | `int` | Total bytes consumed from input |

### HttpRequestMessage

| Property | Type | Description |
|----------|------|-------------|
| `Method` | `string` | The request method, case-sensitive ("GET", "POST", etc.) |
| `RequestTarget` | `string` | The request-target as received ("/path?query") |
| `RequestTargetForm` | `RequestTargetForm` | Origin, Absolute, Authority, or Asterisk |
| `Version` | `string` | "HTTP/1.1" or "HTTP/1.0" |
| `Headers` | `HttpHeaderCollection` | The header fields |
| `Body` | `byte[]` | Decoded body content (null if no body) |
| `RawBody` | `byte[]` | Raw body bytes as received on the wire |
| `Trailers` | `HttpHeaderCollection` | Trailer fields after chunked body |
| `BodyFraming` | `BodyFrameKind` | How the body length was determined |

### HttpResponseMessage

| Property | Type | Description |
|----------|------|-------------|
| `StatusCode` | `int` | The 3-digit status code (200, 404, etc.) |
| `ReasonPhrase` | `string` | The reason phrase ("OK", "Not Found") |
| `Version` | `string` | "HTTP/1.1" or "HTTP/1.0" |
| `Headers` | `HttpHeaderCollection` | The header fields |
| `Body` | `byte[]` | Decoded body content (null if no body) |
| `Trailers` | `HttpHeaderCollection` | Trailer fields after chunked body |
| `BodyFraming` | `BodyFrameKind` | How the body length was determined |

### HttpHeaderCollection

Preserves wire order. Case-insensitive keyed access.

| Method | Description |
|--------|-------------|
| `headers["Name"]` | Get combined value (comma-joined if multiple). Returns null if absent. |
| `headers.GetValues("Name")` | Get all individual values as `string[]` |
| `headers.GetHeaders("Name")` | Get all `HttpHeader` entries with raw bytes |
| `headers.Contains("Name")` | Check if a field name exists |
| `headers[index]` | Access by wire-order index |
| `headers.Count` | Total number of header lines |
| `headers.GetFieldNames()` | Distinct field names in first-occurrence order |

## Configuration

`ParserOptions` controls the parser's behavior. All size limits are in bytes.

| Option | Default | Description |
|--------|---------|-------------|
| `MaxStartLineLength` | 8192 | Maximum request-line or status-line length. RFC 9112 recommends at least 8000. |
| `MaxHeaderLineLength` | 8192 | Maximum length of a single header field line |
| `MaxHeaderCount` | 100 | Maximum number of header field lines |
| `MaxHeaderSectionSize` | 65536 | Maximum total header section size (64 KB) |
| `MaxBodySize` | -1 (unlimited) | Maximum message body size |
| `MaxChunkSize` | 0x7FFFFFFF | Maximum chunk size in chunked encoding |
| `RejectObsoleteLineFolding` | true | Reject obs-fold in headers (Section 5.2) |
| `RejectConflictingFraming` | true | Reject messages with both TE and CL (Section 6.3 rule 3) |
| `StrictMode` | false | Reject all deviations from strict RFC compliance |

Presets:

```csharp
// Default: lenient, production-safe limits
var opts = new ParserOptions();

// Strict: reject everything the RFC says SHOULD be rejected
var opts = ParserOptions.Strict;

// Custom: tune for your use case
var opts = new ParserOptions
{
    MaxHeaderCount = 50,
    MaxBodySize = 1024 * 1024, // 1 MB
    RejectConflictingFraming = true,
    StrictMode = false
};
```

## Callbacks

Four hooks for integration during parsing. Each callback returns `bool` — returning `false` aborts parsing immediately with `ParseErrorKind.RejectedByCallback`.

**OnStartLineParsed** — called after the request-line or status-line is parsed. Use for early rejection of unwanted methods or targets.

```csharp
var opts = new ParserOptions
{
    OnStartLineParsed = (method, target, version) =>
    {
        // Reject anything that isn't GET or POST
        return method == "GET" || method == "POST";
    }
};
```

**OnHeaderParsed** — called after each header field line. Use for security filtering.

```csharp
var opts = new ParserOptions
{
    OnHeaderParsed = (name, value, index) =>
    {
        // Reject oversized cookie headers
        if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) && value.Length > 4096)
            return false;
        return true;
    }
};
```

**OnChunkHeader** — called when a chunk header is read during chunked decoding. Use for progress monitoring or early termination.

**OnBodyProgress** — called during body reading. Use for progress bars or timeout enforcement.

## Warnings and Errors

cshttp uses a two-tier reporting system.

**ParseError** — a fatal condition. Parsing stops. The `ParseResult.Error` property contains the error kind, the byte position where it was detected, and a human-readable message.

```csharp
if (!result.Success)
{
    Console.WriteLine(result.Error.Kind);     // ParseErrorKind.ConflictingFraming
    Console.WriteLine(result.Error.Position); // 47
    Console.WriteLine(result.Error.Message);  // "Request has both Transfer-Encoding and..."
}
```

**ParseWarning** — a non-fatal deviation that was tolerated. Parsing continues. Warnings are collected in `ParseResult.Warnings` even on success.

```csharp
foreach (var warn in result.Warnings)
{
    Console.WriteLine(warn.Kind);     // ParseWarningKind.LeadingEmptyLines
    Console.WriteLine(warn.Position); // 0
    Console.WriteLine(warn.Message);  // "Empty lines preceded the start-line."
}
```

When the RFC says MUST, the parser enforces it as an error. When the RFC says SHOULD, the parser enforces it in strict mode and tolerates it in lenient mode — but always records it.

## Security

cshttp handles the HTTP edge cases that hand-written parsers typically miss — the edge cases that become attack vectors.

**Request smuggling** — when both `Transfer-Encoding` and `Content-Length` are present, the parser detects the conflict and rejects the message (configurable). This prevents the class of attacks where a proxy and a backend disagree about where one request ends and the next begins.

**Oversized fields** — configurable limits on start-line length, header count, header line length, total header section size, body size, and individual chunk size. Each limit has a sensible default. Each produces a specific error kind when exceeded.

**Malformed chunk encoding** — the parser validates hex chunk sizes, enforces trailing CRLF after chunk data, detects overflow in chunk size values, and rejects non-hex bytes in chunk size lines.

**Bare CR injection** — bare CR (CR not followed by LF) in header names is always rejected. In header values, strict mode rejects it; lenient mode replaces it with SP and records a warning.

**Obsolete line folding** — obs-fold (continuation lines starting with SP or HTAB) is rejected by default. When permitted, it is replaced with SP per RFC 9112 Section 5.2, never silently concatenated.

**Content-Length validation** — non-decimal values are rejected. Comma-separated duplicate values are accepted only if all values are identical (per RFC 9112 Section 6.3 rule 5). Conflicting values within the same header are rejected.

## Limitations

cshttp is a message parser. It is deliberately scoped to a single concern. The following are outside its scope:

- **Connection management** — cshttp performs no I/O. It does not open sockets, manage connections, or handle keep-alive. You supply bytes; it parses them.
- **TLS/SSL** — encryption is a transport concern. cshttp operates on decrypted bytes.
- **HTTP/2 and HTTP/3** — these protocols use binary framing, not the text-based format of HTTP/1.1. They require different parsers.
- **Content decompression** — `Content-Encoding: gzip` applies to the representation, not the message framing. cshttp decodes `Transfer-Encoding: chunked` (which is message framing) but does not decompress gzip/deflate body content.
- **URI decoding** — the `RequestTarget` is returned exactly as received on the wire. Percent-decoding, query string parsing, and path normalization are left to the consumer.
- **Cookie parsing** — cookies are header values. cshttp gives you the raw `Cookie` or `Set-Cookie` header value. Parsing the name-value pairs is left to the consumer.
- **Multipart form parsing** — `multipart/form-data` is a content type, not a message framing concern. cshttp gives you the body bytes. Parsing the MIME boundaries is left to the consumer.
- **Streaming / incremental parsing** — the current implementation requires the complete message in a single `byte[]` buffer. Incremental parsing across multiple reads is a planned future enhancement.

These are deliberate scope boundaries. cshttp does one thing — parsing the HTTP/1.1 message envelope — and does it completely.

## Specification Coverage

| RFC 9112 Section | Feature | Implementation |
|------------------|---------|---------------|
| Section 2.1 | Message format (start-line, headers, body) | Full parsing pipeline |
| Section 2.2 | Leading empty lines tolerated | `SkipLeadingEmptyLines` with warning |
| Section 2.2 | Bare LF as line terminator | Tolerated in lenient mode with warning |
| Section 2.2 | Bare CR detection | Rejected in names; configurable in values |
| Section 2.2 | Whitespace after start-line | Rejected strict; consumed lenient |
| Section 2.3 | HTTP-version validation | Case-sensitive, exact format enforced |
| Section 3 | Request-line parsing | method SP request-target SP HTTP-version |
| Section 3.2 | Request-target forms | Origin, Absolute, Authority, Asterisk |
| Section 4 | Status-line parsing | HTTP-version SP status-code SP reason-phrase |
| Section 5 | Header field syntax | field-name ":" OWS field-value OWS |
| Section 5.1 | No whitespace before colon | Enforced as error |
| Section 5.2 | Obsolete line folding | Rejected by default; replaceable |
| Section 6.2 | Content-Length framing | Exact byte-count body reading |
| Section 6.3 Rule 1 | HEAD/1xx/204/304 no body | `BodyFrameKind.NoBodyByStatus` |
| Section 6.3 Rule 2 | CONNECT 2xx tunnel | `BodyFrameKind.Tunnel` |
| Section 6.3 Rule 3 | TE + CL conflict | Rejected or TE overrides CL |
| Section 6.3 Rule 4 | Chunked transfer coding | Full chunk parsing with trailers |
| Section 6.3 Rule 5 | Duplicate Content-Length | Identical values collapsed; conflicts rejected |
| Section 6.3 Rule 7 | No TE, no CL on request | Zero body by absence |
| Section 6.3 Rule 8 | No framing on response | Read until close |
| Section 7.1 | Chunked encoding | Hex size, chunk data, CRLF, terminal chunk |
| Section 7.1.1 | Chunk extensions | Ignored with warning |
| Section 7.1.2 | Trailer headers | Parsed into `Trailers` collection |

## Who This Is For

- **Custom servers** — build a web server from `TcpListener` and raw sockets with production-grade HTTP parsing
- **Reverse proxies and API gateways** — read requests from one socket, inspect and route them, forward to another
- **IoT and embedded devices** — HTTP endpoints on resource-constrained .NET runtimes where ASP.NET Core is too heavy
- **Testing tools** — mock servers, traffic recorders, protocol analyzers that parse captured HTTP bytes from files or packet captures
- **Webhook receivers** — lightweight console applications that listen for POST requests from external services
- **Educational use** — learn HTTP at the byte level with a parser that shows exactly what the specification requires

cshttp does not replace ASP.NET Core. It fills a gap that makes ASP.NET Core *optional* for scenarios where it was previously the only path to correct HTTP parsing.

## License

**Public Domain.** No rights reserved. No conditions. No restrictions. Use it for any purpose, without attribution, without permission.

---

*cshttp is built against [RFC 9112: HTTP/1.1](https://www.rfc-editor.org/rfc/rfc9112) and [RFC 9110: HTTP Semantics](https://www.rfc-editor.org/rfc/rfc9110), published by the IETF.*
