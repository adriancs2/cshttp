![cshttp banner](https://raw.githubusercontent.com/wiki/adriancs2/cshttp/CSHTTP%20Tutorial/cover.png)

 # cshttp

> **A fast, zero-dependency, pure C# HTTP/1.1 micro web server and toolkit. Built-in wire parser and classic Request/Response API. Runs on .NET Framework 4.8 and .NET 8/10+.**

---

## The "Aha!" Moment

No IIS. No ASP.NET Core runtime setup. No NuGet dependency graph. Just pure C# and one DLL:

```csharp
using cshttp;

static class Program
{
    static void Main()
    {
        // Starts the server on port 8080 and handles incoming requests
        WebApp.Run(8080, OnRequest);
    }

    static void OnRequest()
    {
        string path = Request.Path.ToLower();

        switch (path)
        {
            case "/":
            case "/home":
                RenderHome();
                break;

            default:
                RenderNotFound();
                break;
        }
    }

    static void RenderHome()
    {
        string html = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Welcome - cshttp</title>
</head>
<body>
    <main>
        <h1>Welcome to cshttp!</h1>
        <p>Hello World!</p>
    </main>
</body>
</html>";

        Response.Write(html);
    }

    static void RenderNotFound()
    {
        Response.StatusCode = 404;
        string html = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>404 Not Found - cshttp</title>
</head>
<body>
    <main>
        <h1>404 Not Found</h1>
        <p>The page you are looking for does not exist.</p>
    </main>
</body>
</html>";

        Response.Write(html);
    }
}
```

Press **F5** (or `dotnet run`), open `http://localhost:8080/`, and your web server is live.

---

## What is cshttp?

`cshttp` is a self-sustained, embedded micro web server for C#. 

It packs a **zero-allocation HTTP wire parser**, a **classic ASP.NET-style Request/Response toolkit**, and a **high-concurrency IOCP socket listener** into a single, compact DLL (~105 KB).

### Why use it?
- **Zero Dependencies**: Pure C#. Works anywhere .NET runs.
- **Dual Runtime Support**: Runs out of the box on both **classic .NET Framework 4.8** and **modern .NET (8.0, 9.0, 10.0+)**.
- **Embeddable**: Drop a fast web dashboard, admin UI, or REST API into any Windows Service, desktop application, game, or utility without pulling in megabytes of web framework dependencies.
- **Familiar & Intuitive**: Uses the battle-tested, classic `Request` and `Response` pattern that every developer intuitively understands.

---

## Core Foundations: The Universal Web APIs

Everything on the web—no matter how complex—is built on a few primitive concepts. `cshttp` gives you direct, unabstracted access to all of them:

### 1. Routing by Path
Inspect the requested URL and route traffic using standard C# `switch` or `if` statements:
```csharp
string path = Request.Path.ToLower();
```

### 2. URL Parameters (`Request.QueryString`)
Read values from the URL query string (e.g. `/greet?name=Alice`):
```csharp
string name = Request.QueryString["name"];
```

### 3. Form Submissions (`Request.Form`)
Read POST form data submitted by HTML forms:
```csharp
if (Request.HttpMethod == "POST")
{
    string author  = Request.Form["author"];
    string comment = Request.Form["comment"];
}
```

### 4. Visitor State & Memory (`Request.Cookies` & `Response.Cookies`)
Remember visitors across browser requests:
```csharp
// Reading a cookie:
string visitorId = Request.Cookies["user_id"]?.Value;

// Issuing a cookie:
HttpCookie cookie = new HttpCookie("user_id", "12345");
cookie.Expires = DateTime.Now.AddDays(30);
Response.Cookies.Add(cookie);
```

### 5. Raw Payloads & APIs (`Request.BodyText` & `Request.BodyBytes`)
Read raw text or binary payloads in a single line without stream boilerplate:
```csharp
// Pure text (JSON, XML, CSV, plain text):
string payload = Request.BodyText;

// Pure bytes (Protobuf, images, binary streams):
byte[] rawBytes = Request.BodyBytes;

// Or stream directly (zero copy):
Stream stream = Request.InputStream;
```

### 6. HTTP Status Codes (`Response.StatusCode`)
Set standard HTTP responses:
```csharp
Response.StatusCode = 200; // OK
Response.StatusCode = 404; // Not Found
Response.StatusCode = 500; // Internal Server Error
```

### 7. Outputting Text & HTML (`Response.Write`)
Send HTML, JSON strings, CSS, or plain text back to the browser:
```csharp
Response.ContentType = "application/json";
Response.Write("{\"status\":\"ok\"}");
```

### 8. Outputting Binary Data (`Response.BinaryWrite` & `Response.OutputStream`)
Send pure bytes, generated images, or stream directly from another stream:
```csharp
// Classic full-buffer write:
byte[] imageBytes = File.ReadAllBytes("chart.png");
Response.ContentType = "image/png";
Response.BinaryWrite(imageBytes);

// High-performance slice write:
Response.WriteBytes(buffer, offset, count);

// Pipe any stream directly (e.g. GZipStream, MemoryStream):
myStream.CopyTo(Response.OutputStream);
```

### 9. High-Speed File Streaming (`Response.TransmitFile`)
Stream static files, images, PDFs, and downloads directly from disk with zero memory copies:
```csharp
Response.ContentType = "image/png";
Response.TransmitFile(WebApp.MapPath("~/assets/logo.png"));
```

### 10. File Uploads (`Request.Files`)
Built-in multipart parser handles single or multiple files without third-party dependencies:
```csharp
// 1. Single file by input name:
HttpPostedFile avatar = Request.Files["avatar"];
avatar?.SaveAs(WebApp.MapPath("~/uploads/" + avatar.FileName));

// 2. Multiple files from one input (<input type="file" name="photos" multiple />):
foreach (HttpPostedFile photo in Request.Files.GetMultiple("photos"))
{
    photo.SaveAs(WebApp.MapPath("~/uploads/" + photo.FileName));
}

// 3. Or loop through all uploaded files:
for (int i = 0; i < Request.Files.Count; i++)
{
    Request.Files[i].SaveAs(WebApp.MapPath("~/uploads/" + Request.Files[i].FileName));
}
```

### 11. Redirection (`Response.Redirect`)
Classic 302 and 301 redirects for the Post-Redirect-Get pattern:
```csharp
Response.Redirect("/home");
Response.RedirectPermanent("/new-page");
```

### 12. Safe Background Work (`WebApp.BackgroundWork`)
Run tasks in the background that are safely tracked during OS/IIS graceful shutdown:
```csharp
WebApp.BackgroundWork(ct =>
{
    // Send emails, process logs, audit records
    // The server waits for this task before process termination!
});
```

### 13. Security & Utilities (`HttpUtility`)
Zero-dependency XSS protection, URL encoding, and JavaScript string escaping:
```csharp
string safeHtml = HttpUtility.HtmlEncode(untrustedInput);
string safeUrl  = HttpUtility.UrlEncode("search query");
```

---

## Quick Start (3 Options)

### Option 1: Download the Starter Pack (Recommended)
Download the pre-configured **[cshttp-starter-pack.zip](https://github.com/adriancs2/cshttp/releases)** from Releases. It includes the pre-compiled dual-runtime DLL and a pre-configured project. Unzip and run!

### Option 2: Add `cshttp.dll` to Your Project
1. Grab `cshttp.dll` from Releases (or build from source).
2. Reference it in your `.csproj`:
   ```xml
   <ItemGroup>
     <Reference Include="cshttp"><HintPath>lib\cshttp.dll</HintPath></Reference>
   </ItemGroup>
   ```

### Option 3: Direct Source Inclusion
Drop the `Core/`, `Web/`, and `Server/` folders directly into your solution for full debugging control with zero compiled binary dependencies.

---

## Documentation & Tutorials

Wiki Link: [https://github.com/adriancs2/cshttp/wiki](https://github.com/adriancs2/cshttp/wiki)

Wiki Content:

- **15-Day Beginner Tutorial Series**: A complete, step-by-step course building web apps from scratch.
- **Architecture & Structure Overview**: Deep dive into the internal IOCP socket listener, wire parser, and zero-allocation pipelines.
- **Benchmark Report**: High-concurrency throughput and latency benchmarks.
- Tier 1 Complete Reference: cshttp.core — The RFC 9112 Wire Engine
- Tier 2 Deep Dive: cshttp.web - Content Toolkit & System.Web Drop-In
- Tier 3 Deep Dive: cshttp.server 1.0 — High-Concurrency Standalone Web Server

---

## License

The Unlicense (Public Domain). Free and unencumbered software released into the public domain for any commercial or non-commercial use.
