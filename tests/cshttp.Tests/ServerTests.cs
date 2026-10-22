using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace cshttp.Tests
{
    /// <summary>One shared server for most integration tests, with Pageless-style switch routing.</summary>
    static class ServerFixture
    {
        static readonly object Sync = new object();
        static HttpServer _server;
        static IPEndPoint _ep;
        public static string RootDir;
        public static int BackgroundCount;

        public static IPEndPoint Ep
        {
            get
            {
                lock (Sync)
                {
                    if (_server == null) Start();
                    return _ep;
                }
            }
        }

        public static HttpServer Server { get { var e = Ep; return _server; } }

        static void Start()
        {
            RootDir = Path.Combine(Path.GetTempPath(), "cshttp-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(RootDir, "static", "sub"));
            File.WriteAllText(Path.Combine(RootDir, "static", "site.css"), "body { color: red; }");
            File.WriteAllText(Path.Combine(RootDir, "static", "web.config"), "<configuration/>");
            File.WriteAllText(Path.Combine(RootDir, "static", "sub", "a b.txt"), "spaced");
            File.WriteAllText(Path.Combine(RootDir, "secret.txt"), "TOP SECRET");
            var o = new ServerOptions { ContentRoot = RootDir, MaxRequestBodySize = 5 * 1024 * 1024, ServerHeader = "cshttp-test" };
            o.ListenHttp(IPAddress.Loopback, 0);
            _server = new HttpServer(o);
            _server.Start(Handler);
            _ep = _server.LocalEndpoints[0];
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (_server == null) return;
                _server.Stop();
                try { Directory.Delete(RootDir, true); } catch { }
                _server = null;
            }
        }

        public static HttpServer StartCustom(ServerOptions o, Action<HttpContext> handler = null)
        {
            o.ListenHttp(IPAddress.Loopback, 0);
            var s = new HttpServer(o);
            s.Start(handler ?? Handler);
            return s;
        }

        /// <summary>Routing exactly like Global.asax.cs in the Pageless architecture.</summary>
        public static void Handler(HttpContext ctx)
        {
            HttpRequest Request = ctx.Request;
            HttpResponse Response = ctx.Response;
            string path = Request.Path.ToLowerInvariant().Trim().TrimEnd('/');

            if (path.StartsWith("/static/"))
            {
                string filePath = WebApp.MapPath(Request.Path);
                if (!Response.TransmitFile(filePath))
                {
                    Response.StatusCode = 404;
                }
                return;
            }

            switch (path)
            {
                case "/hello": Response.Write("hello"); return;
                case "/echo":
                    Response.ContentType = "text/plain";
                    Response.Write(Request.HttpMethod + "|" + Request.Path + "|" + Request.QueryString["a"] + "|" + Request.Form["a"] + "|" + Request.Files.Count + "|" + Request.ContentLength);
                    return;
                case "/upload":
                {
                    var sb = new StringBuilder();
                    sb.Append(Request.Form["note"]);
                    foreach (string key in Request.Files.AllKeys)
                    {
                        HttpPostedFile f = Request.Files[key];
                        sb.Append('|').Append(key).Append(':').Append(f.FileName).Append(':').Append(f.ContentLength).Append(':').Append(Sha(f.InputStream));
                    }
                    Response.Write(sb.ToString());
                    return;
                }
                case "/bodyecho":
                    Response.ContentType = "application/octet-stream";
                    Response.BinaryWrite(Request.BinaryRead(Request.TotalBytes));
                    return;
                case "/stream":
                    Response.BufferOutput = false;
                    Response.Write("a");
                    Response.Write("b");
                    Response.Write("c");
                    return;
                case "/flush":
                    Response.Write("x");
                    Response.Flush();
                    Response.Write("y");
                    return;
                case "/hearth":
                    Response.ContentType = "application/json";
                    Response.Write("{\"success\":true}");
                    Response.TrySkipIisCustomErrors = true;
                    try { Response.Flush(); } catch { }
                    Response.SuppressContent = true;
                    ctx.ApplicationInstance.CompleteRequest();
                    return;
                case "/throw": throw new InvalidOperationException("boom");
                case "/throw400": throw new HttpException(400, "bad <thing>");
                case "/throwafterflush":
                    Response.Write("partial");
                    Response.Flush();
                    throw new InvalidOperationException("late failure");
                case "/badcookie":
                    Response.Cookies.Add(new HttpCookie("a", "x\r\nSet-Cookie: evil=1"));
                    Response.Write("never");
                    return;
                case "/end":
                    Response.Write("a");
                    try { Response.End(); } catch (Exception) { }
                    Response.Write("b");
                    return;
                case "/redirect": Response.Redirect("/hello", false); return;
                case "/cookie":
                    Response.Cookies.Add(new HttpCookie("sid", "abc") { HttpOnly = true, SameSite = SameSiteMode.Lax });
                    Response.Write("ok");
                    return;
                case "/big": Response.Write(new string('x', 1024 * 1024)); return;
                case "/ip": Response.Write(Request.UserHostAddress + "|" + Request.IsSecureConnection + "|" + Request.IsLocal); return;
                case "/current": Response.Write(ReferenceEquals(HttpContext.Current, ctx) ? "same" : "diff"); return;
                case "/slow":
                    Thread.Sleep(int.Parse(Request.QueryString["ms"] ?? "500"));
                    Response.Write("slow");
                    return;
                case "/bg":
                    HostingEnvironment.QueueBackgroundWorkItem(ct => Interlocked.Increment(ref BackgroundCount));
                    Response.Write("queued");
                    return;
                case "/notmodified":
                    Response.Cache.SetCacheability(HttpCacheability.Private);
                    Response.AddHeader("ETag", "\"abc\"");
                    if (Request.Headers["If-None-Match"] == "\"abc\"") Response.StatusCode = 304;
                    else Response.Write("fresh");
                    try { Response.Flush(); } catch { }
                    Response.SuppressContent = true;
                    ctx.ApplicationInstance.CompleteRequest();
                    return;
                case "/mappath": Response.Write(HostingEnvironment.MapPath("~/static/site.css")); return;
            }
            Response.StatusCode = 404;
            Response.Write("nf");
        }

        public static string Sha(Stream s)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "");
        }

        public static string Sha(byte[] b)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "");
        }
    }

    static class ServerTests
    {
        static RawClient Client(int timeoutMs = 5000) { return new RawClient(ServerFixture.Ep, timeoutMs); }

        public static void Register(TestList t)
        {
            t.Add("server: GET basics", () =>
            {
                using (var c = Client())
                {
                    var r = c.Get("/hello");
                    A.Eq(200, r.Status);
                    A.Eq("hello", r.Text);
                    A.Eq("5", r.Header("Content-Length"));
                    A.Eq("cshttp-test", r.Header("Server"));
                    A.True(r.Header("Date") != null, "Date header");
                    A.Eq(404, c.Get("/nope").Status, "unknown route");
                }
            });

            t.Add("server: keep-alive reuses the connection", () =>
            {
                using (var c = Client())
                {
                    for (int i = 0; i < 20; i++)
                    {
                        var r = c.Get("/echo?a=" + i);
                        A.Eq("GET|/echo|" + i + "||0|0", r.Text, "request " + i);
                    }
                    A.Eq(1L, 1L);
                }
            });

            t.Add("server: pipelined requests answered in order", () =>
            {
                using (var c = Client())
                {
                    c.Send("GET /echo?a=1 HTTP/1.1\r\nHost: h\r\n\r\nGET /hello HTTP/1.1\r\nHost: h\r\n\r\nGET /echo?a=3 HTTP/1.1\r\nHost: h\r\n\r\n");
                    A.Eq("GET|/echo|1||0|0", c.Read().Text);
                    A.Eq("hello", c.Read().Text);
                    A.Eq("GET|/echo|3||0|0", c.Read().Text);
                }
            });

            t.Add("server: request delivered one byte at a time", () =>
            {
                using (var c = Client())
                {
                    byte[] req = Bytes.L("POST /bodyecho HTTP/1.1\r\nHost: h\r\nContent-Length: 11\r\n\r\nhello world");
                    foreach (byte b in req) c.Send(new[] { b });
                    var r = c.Read();
                    A.Eq(200, r.Status);
                    A.Eq("hello world", r.Text);
                }
            });

            t.Add("server: Connection close and HTTP/1.0", () =>
            {
                using (var c = Client())
                {
                    c.Send("GET /hello HTTP/1.1\r\nHost: h\r\nConnection: close\r\n\r\n");
                    var r = c.Read();
                    A.Eq("close", r.Header("Connection"));
                    A.True(c.WaitForClose(2000), "server closed");
                }
                using (var c = Client())
                {
                    c.Send("GET /hello HTTP/1.0\r\n\r\n");
                    A.Eq("hello", c.Read().Text);
                    A.True(c.WaitForClose(2000), "1.0 closes");
                }
                using (var c = Client())
                {
                    c.Send("GET /hello HTTP/1.0\r\nConnection: keep-alive\r\n\r\n");
                    A.Eq("keep-alive", c.Read().Header("Connection"));
                    c.Send("GET /hello HTTP/1.0\r\nConnection: keep-alive\r\n\r\n");
                    A.Eq("hello", c.Read().Text, "second request on 1.0 keep-alive");
                }
            });

            t.Add("server: protocol errors answer and close", () =>
            {
                ExpectErrorAndClose("GET / HTTP/1.1\r\n\r\n", 400, "missing host");
                ExpectErrorAndClose("GET / HTTP/1.1\nHost: h\n\n", 400, "bare LF");
                ExpectErrorAndClose("GET / HTTP/3.0\r\nHost: h\r\n\r\n", 505, "version");
                ExpectErrorAndClose("GET /" + new string('a', 9000) + " HTTP/1.1\r\nHost: h\r\n\r\n", 414, "long line");
                ExpectErrorAndClose("GET / HTTP/1.1\r\nHost: h\r\nX: " + new string('a', 40000) + "\r\n\r\n", 431, "big headers");
                ExpectErrorAndClose("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: gzip, chunked\r\n\r\n", 501, "unsupported coding");
                ExpectErrorAndClose("\x16\x03\x01\x02\x00\x01\x00\x01\xfc\x03\x03\r\n\r\n", 400, "TLS hello on plain port");
                ExpectErrorAndClose("POST /bodyecho HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\nZZ\r\n", 400, "bad chunk");
            });

            t.Add("server: smuggling attempt gets one 400 and nothing else", () =>
            {
                using (var c = Client())
                {
                    c.Send("POST /echo HTTP/1.1\r\nHost: h\r\nContent-Length: 6\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\nGET /hello HTTP/1.1\r\nHost: h\r\n\r\n");
                    var r = c.Read();
                    A.Eq(400, r.Status);
                    A.Eq("close", r.Header("Connection"));
                    A.True(c.WaitForClose(2000), "closed without serving the smuggled request");
                }
            });

            t.Add("server: body limit 413", () =>
            {
                ExpectErrorAndClose("POST /bodyecho HTTP/1.1\r\nHost: h\r\nContent-Length: 6000000\r\n\r\n", 413, "declared too large");
                using (var s = ServerFixture.StartCustom(new ServerOptions { MaxRequestBodySize = 100 }))
                using (var c = new RawClient(s.LocalEndpoints[0]))
                {
                    c.Send("POST /bodyecho HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n64\r\n" + new string('a', 100) + "\r\n1\r\nb\r\n0\r\n\r\n");
                    A.Eq(413, c.Read().Status, "chunked too large");
                }
            });

            t.Add("server: Expect 100-continue", () =>
            {
                using (var c = Client())
                {
                    c.Send("POST /bodyecho HTTP/1.1\r\nHost: h\r\nExpect: 100-continue\r\nContent-Length: 5\r\n\r\n");
                    var interim = c.Read();
                    A.Eq(100, interim.Status, "interim response");
                    c.Send("hello");
                    var r = c.Read();
                    A.Eq(200, r.Status);
                    A.Eq("hello", r.Text);
                }
            });

            t.Add("server: chunked request body", () =>
            {
                using (var c = Client())
                {
                    c.Send("POST /bodyecho HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n6;x=y\r\n world\r\n0\r\nTrailer: t\r\n\r\n");
                    A.Eq("hello world", c.Read().Text);
                    A.Eq("hello", c.Get("/hello").Text, "connection still in sync");
                }
            });

            t.Add("server: urlencoded form and multipart upload", () =>
            {
                using (var c = Client())
                {
                    c.Send("POST /echo?a=q HTTP/1.1\r\nHost: h\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 9\r\n\r\na=f%20orm");
                    A.Eq("POST|/echo|q|f orm|0|9", c.Read().Text);

                    byte[] file = new byte[3 * 1024 * 1024];
                    new Random(7).NextBytes(file);
                    byte[] body = WebTests.BuildMultipart("----cshttpBoundary", WebTests.Field("note", "n\u00f6te"), WebTests.FilePart("doc", "big.bin", "application/octet-stream", file));
                    c.Send("POST /upload HTTP/1.1\r\nHost: h\r\nContent-Type: multipart/form-data; boundary=----cshttpBoundary\r\nContent-Length: " + body.Length + "\r\n\r\n");
                    c.Send(body);
                    A.Eq("n\u00f6te|doc:big.bin:" + file.Length + ":" + ServerFixture.Sha(file), c.Read().Text);
                }
            });

            t.Add("server: malformed multipart gives 400 and keeps the connection", () =>
            {
                using (var c = Client())
                {
                    string body = "--B\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nnever-terminated";
                    c.Send("POST /echo HTTP/1.1\r\nHost: h\r\nContent-Type: multipart/form-data; boundary=B\r\nContent-Length: " + body.Length + "\r\n\r\n" + body);
                    var r = c.Read();
                    A.Eq(400, r.Status);
                    A.Eq("hello", c.Get("/hello").Text);
                }
            });

            t.Add("server: HEAD sends headers only", () =>
            {
                using (var c = Client())
                {
                    c.Send("HEAD /hello HTTP/1.1\r\nHost: h\r\n\r\n");
                    var r = c.Read(true);
                    A.Eq("5", r.Header("Content-Length"));
                    A.Eq("hello", c.Get("/hello").Text, "no stray body bytes");
                }
            });

            t.Add("server: streaming, flush and Hearth EndResponse pattern", () =>
            {
                using (var c = Client())
                {
                    var r = c.Get("/stream");
                    A.True(r.WasChunked, "chunked");
                    A.Eq("abc", r.Text);
                    r = c.Get("/flush");
                    A.Eq("xy", r.Text);
                    r = c.Get("/hearth");
                    A.Eq("{\"success\":true}", r.Text);
                    A.Eq("application/json; charset=utf-8", r.Header("Content-Type"));
                    A.Eq("hello", c.Get("/hello").Text);
                }
            });

            t.Add("server: 304 sent via Flush keeps the connection in sync", () =>
            {
                using (var c = Client())
                {
                    var r = c.Get("/notmodified");
                    A.Eq("fresh", r.Text);
                    r = c.Get("/notmodified", "If-None-Match: \"abc\"\r\n");
                    A.Eq(304, r.Status);
                    A.Eq(null, r.Header("Transfer-Encoding"), "no framing on 304");
                    A.Eq("hello", c.Get("/hello").Text, "next request parses cleanly");
                }
            });
            t.Add("server: handler exceptions", () =>
            {
                using (var c = Client())
                {
                    var r = c.Get("/throw");
                    A.Eq(500, r.Status);
                    A.NotContains(r.Text, "boom", "no details unless DetailedErrors");
                    r = c.Get("/throw400");
                    A.Eq(400, r.Status);
                    A.Contains(r.Text, "bad &lt;thing&gt;");
                    r = c.Get("/badcookie");
                    A.Eq(500, r.Status, "invalid header at send time");
                    A.Eq(0, r.HeaderCount("Set-Cookie"));
                    A.Eq("hello", c.Get("/hello").Text, "connection survives");
                }
                using (var c = Client())
                {
                    c.Send("GET /throwafterflush HTTP/1.1\r\nHost: h\r\n\r\n");
                    A.Throws<IOException>(() => c.Read(), "truncated response");
                }
                using (var c = Client()) A.Eq("hello", c.Get("/hello").Text, "server alive");
            });

            t.Add("server: End, redirect, cookies, large body", () =>
            {
                using (var c = Client())
                {
                    A.Eq("a", c.Get("/end").Text);
                    var r = c.Get("/redirect");
                    A.Eq(302, r.Status);
                    A.Eq("/hello", r.Header("Location"));
                    r = c.Get("/cookie");
                    A.Eq("sid=abc; path=/; HttpOnly; SameSite=Lax", r.Header("Set-Cookie"));
                    r = c.Get("/big");
                    A.Eq(1024 * 1024, r.Body.Length);
                    A.Eq("1048576", r.Header("Content-Length"));
                }
            });

            t.Add("server: context, client info and MapPath", () =>
            {
                using (var c = Client())
                {
                    A.Eq("same", c.Get("/current").Text);
                    A.Eq("127.0.0.1|False|True", c.Get("/ip").Text);
                    A.Eq(Path.Combine(ServerFixture.RootDir, "static", "site.css"), c.Get("/mappath").Text);
                    A.Eq("127.0.0.1|False|True", c.Get("/ip", "X-Forwarded-For: 203.0.113.5\r\nX-Forwarded-Proto: https\r\n").Text, "untrusted proxy headers ignored");
                }
            });

            t.Add("server: trusted proxy headers", () =>
            {
                var o = new ServerOptions();
                o.TrustedProxies.Add(IPAddress.Loopback);
                using (var s = ServerFixture.StartCustom(o))
                using (var c = new RawClient(s.LocalEndpoints[0]))
                {
                    A.Eq("203.0.113.5|True|False", c.Get("/ip", "X-Forwarded-For: 198.51.100.1, 203.0.113.5, 127.0.0.1\r\nX-Forwarded-Proto: https\r\n").Text);
                    A.Eq("127.0.0.1|False|True", c.Get("/ip").Text, "no headers");
                }
            });

            t.Add("server: static files", () =>
            {
                using (var c = Client())
                {
                    var r = c.Get("/static/site.css");
                    A.Eq(200, r.Status);
                    A.Eq("body { color: red; }", r.Text);
                    A.Eq("text/css; charset=utf-8", r.Header("Content-Type"));
                    A.Eq("public, max-age=3600", r.Header("Cache-Control"));
                    A.Eq("bytes", r.Header("Accept-Ranges"));
                    string etag = r.Header("ETag");
                    string lastModified = r.Header("Last-Modified");
                    A.True(etag != null && lastModified != null, "validators");

                    r = c.Get("/static/site.css", "If-None-Match: " + etag + "\r\n");
                    A.Eq(304, r.Status, "etag");
                    r = c.Get("/static/site.css", "If-Modified-Since: " + lastModified + "\r\n");
                    A.Eq(304, r.Status, "date");
                    r = c.Get("/static/site.css?v=20261019");
                    A.Eq("public, max-age=31536000, immutable", r.Header("Cache-Control"));

                    r = c.Get("/static/site.css", "Range: bytes=0-3\r\n");
                    A.Eq(206, r.Status);
                    A.Eq("body", r.Text);
                    A.Eq("bytes 0-3/20", r.Header("Content-Range"));
                    r = c.Get("/static/site.css", "Range: bytes=100-\r\n");
                    A.Eq(416, r.Status);
                    A.Eq("bytes */20", r.Header("Content-Range"));
                    r = c.Get("/static/site.css", "Range: bytes=0-3\r\nIf-Range: \"stale\"\r\n");
                    A.Eq(200, r.Status, "If-Range mismatch sends everything");

                    A.Eq("spaced", c.Get("/static/sub/a%20b.txt").Text);

                    c.Send("HEAD /static/site.css HTTP/1.1\r\nHost: h\r\n\r\n");
                    r = c.Read(true);
                    A.Eq("20", r.Header("Content-Length"));

                    c.Send("POST /static/site.css HTTP/1.1\r\nHost: h\r\nContent-Length: 0\r\n\r\n");
                    r = c.Read();
                    A.Eq(405, r.Status);
                    A.Eq("GET, HEAD", r.Header("Allow"));

                    A.Eq(404, c.Get("/static/missing.css").Status, "missing");
                    A.Eq(404, c.Get("/static/web.config").Status, "unknown type not served");
                }
            });

            t.Add("server: static path traversal attempts", () =>
            {
                string[] attacks =
                {
                    "/static/../secret.txt", "/static/%2e%2e/secret.txt", "/static/..%2fsecret.txt",
                    "/static/..%5csecret.txt", "/static/sub/../../secret.txt", "/static/site.css%00.png",
                    "/static/site.css.", "/static/site.css%20", "/static//site.css", "/static/./site.css",
                    "/static/%2fetc%2fpasswd", "/static/C:%5cWindows%5cwin.ini", "/static/..%c0%afsecret.txt"
                };
                using (var c = Client())
                {
                    foreach (string a in attacks)
                    {
                        var r = c.Get(a);
                        A.Eq(404, r.Status, a);
                        A.NotContains(r.Text, "TOP SECRET", a);
                    }
                }
            });

            t.Add("server: background work", () =>
            {
                HostingEnvironment.BeginHosting(null);
                int before = Volatile.Read(ref ServerFixture.BackgroundCount);
                using (var c = Client()) A.Eq("queued", c.Get("/bg").Text);
                var sw = Stopwatch.StartNew();
                while (Volatile.Read(ref ServerFixture.BackgroundCount) == before && sw.ElapsedMilliseconds < 3000) Thread.Sleep(10);
                A.Eq(before + 1, Volatile.Read(ref ServerFixture.BackgroundCount));
            });

            t.Add("server: concurrent clients", () =>
            {
                int errors = 0;
                string firstError = null;
                var threads = new List<Thread>();
                for (int th = 0; th < 16; th++)
                {
                    int id = th;
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            using (var c = Client(10000))
                            {
                                for (int i = 0; i < 30; i++)
                                {
                                    string v = id + "-" + i;
                                    var r = c.Get("/echo?a=" + v);
                                    if (r.Text != "GET|/echo|" + v + "||0|0") throw new Exception("wrong body " + r.Text);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errors);
                            Interlocked.CompareExchange(ref firstError, ex.Message, null);
                        }
                    });
                    threads.Add(thread);
                    thread.Start();
                }
                foreach (var th in threads) th.Join();
                A.Eq(0, errors, firstError);
            });

            t.Add("server: idle keep-alive and first-request timeouts", () =>
            {
                using (var s = ServerFixture.StartCustom(new ServerOptions { KeepAliveTimeout = TimeSpan.FromSeconds(1), RequestHeadersTimeout = TimeSpan.FromSeconds(1) }))
                {
                    using (var c = new RawClient(s.LocalEndpoints[0]))
                    {
                        A.Eq("hello", c.Get("/hello").Text);
                        var sw = Stopwatch.StartNew();
                        A.True(c.WaitForClose(4000), "idle connection closed");
                        A.True(sw.ElapsedMilliseconds >= 700, "not too early: " + sw.ElapsedMilliseconds);
                    }
                    using (var c = new RawClient(s.LocalEndpoints[0]))
                    {
                        A.True(c.WaitForClose(4000), "silent connection closed");
                    }
                }
            });

            t.Add("server: slowloris headers get 408", () =>
            {
                using (var s = ServerFixture.StartCustom(new ServerOptions { RequestHeadersTimeout = TimeSpan.FromSeconds(1) }))
                using (var c = new RawClient(s.LocalEndpoints[0]))
                {
                    var sw = Stopwatch.StartNew();
                    c.Send("GET /hello HTTP/1.1\r\n");
                    Thread.Sleep(400);
                    c.Send("Host: h\r\n");
                    Thread.Sleep(300);
                    c.Send("X-Drip: 1\r\n");
                    var r = c.Read();
                    A.Eq(408, r.Status);
                    A.True(sw.ElapsedMilliseconds < 2500, "deadline is for the whole head: " + sw.ElapsedMilliseconds);
                }
            });

            t.Add("server: slow and stalled bodies get 408", () =>
            {
                var o = new ServerOptions { MinRequestBodyBytesPerSecond = 1000, MinDataRateGracePeriod = TimeSpan.FromMilliseconds(300) };
                using (var s = ServerFixture.StartCustom(o))
                using (var c = new RawClient(s.LocalEndpoints[0]))
                {
                    c.Send("POST /bodyecho HTTP/1.1\r\nHost: h\r\nContent-Length: 100000\r\n\r\n0123456789");
                    Thread.Sleep(450);
                    c.Send("0123456789");
                    A.Eq(408, c.Read().Status, "too slow");
                }
                using (var s = ServerFixture.StartCustom(new ServerOptions { BodyReadTimeout = TimeSpan.FromMilliseconds(500) }))
                using (var c = new RawClient(s.LocalEndpoints[0]))
                {
                    c.Send("POST /bodyecho HTTP/1.1\r\nHost: h\r\nContent-Length: 10\r\n\r\nab");
                    A.Eq(408, c.Read().Status, "stalled");
                }
            });

            t.Add("server: connection limit", () =>
            {
                using (var s = ServerFixture.StartCustom(new ServerOptions { MaxConnections = 2 }))
                using (var c1 = new RawClient(s.LocalEndpoints[0]))
                using (var c2 = new RawClient(s.LocalEndpoints[0]))
                {
                    var sw = Stopwatch.StartNew();
                    while (s.ActiveConnections < 2 && sw.ElapsedMilliseconds < 2000) Thread.Sleep(5);
                    using (var c3 = new RawClient(s.LocalEndpoints[0]))
                        A.True(c3.WaitForClose(2000), "third connection refused");
                    A.Eq("hello", c1.Get("/hello").Text);
                    A.Eq("hello", c2.Get("/hello").Text);
                }
            });

            t.Add("server: graceful stop finishes running requests", () =>
            {
                var s = ServerFixture.StartCustom(new ServerOptions { ShutdownTimeout = TimeSpan.FromSeconds(5) });
                IPEndPoint ep = s.LocalEndpoints[0];
                using (var idle = new RawClient(ep))
                using (var busy = new RawClient(ep))
                {
                    A.Eq("hello", idle.Get("/hello").Text);
                    busy.Send("GET /slow?ms=700 HTTP/1.1\r\nHost: h\r\n\r\n");
                    Thread.Sleep(150);
                    var stopper = new Thread(s.Stop);
                    var sw = Stopwatch.StartNew();
                    stopper.Start();
                    var r = busy.Read();
                    A.Eq(200, r.Status);
                    A.Eq("slow", r.Text);
                    A.True(idle.WaitForClose(2000), "idle connection closed by stop");
                    stopper.Join();
                    A.True(sw.ElapsedMilliseconds < 4000, "stop did not hang");
                }
                A.Throws<SocketException>(() => new RawClient(ep, 1000).Dispose(), "listener closed");
                HostingEnvironment.BeginHosting(null);
            });

            t.Add("server: HTTPS with a self-signed certificate", () =>
            {
                X509Certificate2 cert = CreateCertificate();
                var o = new ServerOptions();
                o.ListenHttps(IPAddress.Loopback, 0, cert);
                var s = new HttpServer(o);
                s.Start(ServerFixture.Handler);
                try
                {
                    var tcp = new TcpClient();
                    tcp.Connect(s.LocalEndpoints[0]);
                    tcp.ReceiveTimeout = 5000;
                    var ssl = new SslStream(tcp.GetStream(), false, (a, b, ch, e) => true);
                    ssl.AuthenticateAsClient("localhost");
                    using (var c = new RawClient(tcp, ssl))
                    {
                        A.Eq("127.0.0.1|True|True", c.Get("/ip").Text);
                        A.Eq("hello", c.Get("/hello").Text, "keep-alive over TLS");
                    }
                }
                finally { s.Stop(); HostingEnvironment.BeginHosting(null); }
            });
        }

        static void ExpectErrorAndClose(string raw, int status, string what)
        {
            using (var c = Client())
            {
                c.Send(raw);
                var r = c.Read();
                A.Eq(status, r.Status, what);
                A.Eq("close", r.Header("Connection"), what);
                A.True(c.WaitForClose(3000), what + ": connection closed");
            }
        }

        static X509Certificate2 CreateCertificate()
        {
            using (RSA rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using (X509Certificate2 temp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
                {
                    byte[] pfx = temp.Export(X509ContentType.Pfx, "test");
#if NET9_0_OR_GREATER
                    return X509CertificateLoader.LoadPkcs12(pfx, "test");
#else
                    return new X509Certificate2(pfx, "test");
#endif
                }
            }
        }
    }
}
