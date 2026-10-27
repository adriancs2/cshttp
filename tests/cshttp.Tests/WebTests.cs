using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using static cshttp.Tests.Bytes;

namespace cshttp.Tests
{
    sealed class MemorySink : IHttpResponseSink
    {
        public readonly MemoryStream Stream = new MemoryStream();
        public void Write(byte[] buffer, int offset, int count) { Stream.Write(buffer, offset, count); }
        public void Flush() { }
        public bool IsConnected { get { return true; } }
        public string Text { get { return S(Stream.ToArray()); } }
        public string Body { get { string t = Text; int i = t.IndexOf("\r\n\r\n", StringComparison.Ordinal); return i < 0 ? "" : t.Substring(i + 4); } }
        public string HeadText { get { string t = Text; int i = t.IndexOf("\r\n\r\n", StringComparison.Ordinal); return i < 0 ? t : t.Substring(0, i + 2); } }
    }

    static class WebTests
    {
        public static HttpRequest MakeRequest(string raw, byte[] body = null)
        {
            byte[] b = L(raw);
            HttpRequestHead h;
            var r = HttpRequestParser.ParseHead(b, 0, b.Length, null, out h);
            A.Status(r, 0, "request parse");
            return new HttpRequest(h, body, 0, body == null ? 0 : body.Length, null, null);
        }

        static HttpResponse MakeResponse(out MemorySink sink, string requestLine = "GET / HTTP/1.1\r\nHost: h\r\n\r\n")
        {
            sink = new MemorySink();
            byte[] b = L(requestLine);
            HttpRequestHead h;
            HttpRequestParser.ParseHead(b, 0, b.Length, null, out h);
            return new HttpResponse(sink, h);
        }

        public sealed class Part
        {
            public string Name, Value, FileName, ContentType;
            public byte[] Data;
        }

        public static Part Field(string name, string value) { return new Part { Name = name, Value = value }; }

        public static Part FilePart(string name, string fileName, string contentType, byte[] data)
        {
            return new Part { Name = name, FileName = fileName, ContentType = contentType, Data = data };
        }

        public static byte[] BuildMultipart(string boundary, params Part[] parts)
        {
            var ms = new MemoryStream();
            Action<string> w = s => { byte[] x = Encoding.UTF8.GetBytes(s); ms.Write(x, 0, x.Length); };
            foreach (Part p in parts)
            {
                w("--" + boundary + "\r\n");
                if (p.Data == null)
                {
                    w("Content-Disposition: form-data; name=\"" + p.Name + "\"\r\n\r\n" + p.Value + "\r\n");
                }
                else
                {
                    w("Content-Disposition: form-data; name=\"" + p.Name + "\"; filename=\"" + p.FileName + "\"\r\nContent-Type: " + p.ContentType + "\r\n\r\n");
                    ms.Write(p.Data, 0, p.Data.Length);
                    w("\r\n");
                }
            }
            w("--" + boundary + "--\r\n");
            return ms.ToArray();
        }
        public static void Register(TestList t)
        {
            // ---------------- url encoding ----------------

            t.Add("urlencoded: System.Web semantics", () =>
            {
                var c = new HttpValueCollection();
                UrlEncodedParser.Parse("a=1&b=2&a=3&x&c=%41%zz+b&e=&d=%E4%BD%A0", c, Encoding.UTF8, 0);
                A.Eq("1,3", c["a"]);
                A.Eq("2", c["b"]);
                A.Eq("x", c[null], "segment without '=' has null key");
                A.Eq("A%zz b", c["c"]);
                A.Eq("", c["e"]);
                A.Eq("\u4f60", c["d"]);
                var trailing = new HttpValueCollection();
                UrlEncodedParser.Parse("a=1&", trailing, Encoding.UTF8, 0);
                A.Eq("", trailing[null], "trailing & quirk");
            });

            t.Add("urlencoded: byte parser equals string parser", () =>
            {
                string s = "k1=v+1&k%262=%3D%26&utf=%C3%A9t%C3%A9&raw=\u00e9";
                var a = new HttpValueCollection();
                UrlEncodedParser.Parse(s, a, Encoding.UTF8, 0);
                var b = new HttpValueCollection();
                byte[] bytes = Encoding.UTF8.GetBytes(s);
                UrlEncodedParser.Parse(bytes, 0, bytes.Length, b, Encoding.UTF8, 0);
                A.Eq(a.ToString(), b.ToString());
                A.Eq("=&", b["k&2"]);
                A.Eq("\u00e9t\u00e9", b["utf"]);
                A.Eq("\u00e9", b["raw"]);
            });

            t.Add("urlencoded: key limit", () =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < 20; i++) sb.Append("k" + i + "=1&");
                var c = new HttpValueCollection();
                var ex = A.Throws<HttpException>(() => UrlEncodedParser.Parse(sb.ToString(), c, Encoding.UTF8, 10));
                A.Eq(400, ex.GetHttpCode());
                var same = new HttpValueCollection();
                UrlEncodedParser.Parse("a=1&a=2&a=3&a=4", same, Encoding.UTF8, 1);
                A.Eq("1,2,3,4", same["a"], "repeated key does not count again");
            });

            t.Add("percent decoder: invalid escapes are literal", () =>
            {
                A.Eq("%", PercentDecoder.Decode("%", false, Encoding.UTF8));
                A.Eq("%4", PercentDecoder.Decode("%4", false, Encoding.UTF8));
                A.Eq("%4G", PercentDecoder.Decode("%4G", false, Encoding.UTF8));
                A.Eq("a+b", PercentDecoder.Decode("a+b", false, Encoding.UTF8));
                A.Eq("a b", PercentDecoder.Decode("a+b", true, Encoding.UTF8));
                A.Eq("/a/b c", PercentDecoder.Decode("/a/b%20c", false, Encoding.UTF8));
                A.Eq("\uFFFD", PercentDecoder.Decode("%FF", false, Encoding.UTF8), "invalid utf-8 becomes replacement char");
            });

            // ---------------- cookies ----------------

            t.Add("cookies: request parsing", () =>
            {
                var c = new HttpCookieCollection();
                HttpCookieCollection.ParseInto("a=1; B=two words;c=3;; =x; flag; sid=\"q\"", c);
                A.Eq("1", c["a"].Value);
                A.Eq("two words", c["b"].Value, "case-insensitive lookup");
                A.Eq("3", c["c"].Value);
                A.Eq("\"q\"", c["sid"].Value, "raw value");
                A.Eq(null, c["missing"]);
                A.Eq("x", c[""].Value, "first nameless cookie");
                int names = 0;
                foreach (string n in c) names++;
                A.Eq(c.Count, names, "enumerates names");
            });

            t.Add("cookies: Set-Cookie serialization", () =>
            {
                var k = new HttpCookie("sid", "abc") { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Expires = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
                A.Eq("sid=abc; expires=Wed, 02-Jan-2030 03:04:05 GMT; path=/; secure; HttpOnly; SameSite=Lax", k.ToSetCookieHeader());
                A.Eq("x=; path=/", new HttpCookie("x").ToSetCookieHeader());
                A.Throws<ArgumentException>(() => new HttpCookie("a", "x\r\nSet-Cookie: evil=1").ToSetCookieHeader());
                A.Throws<ArgumentException>(() => new HttpCookie("a", "x; domain=evil").ToSetCookieHeader());
                A.Throws<ArgumentException>(() => new HttpCookie("a b", "x").ToSetCookieHeader());
            });

            // ---------------- header values ----------------

            t.Add("header values: parameters", () =>
            {
                string cd = "form-data; name=\"a;b\"; filename=\"x \\\"q\\\".txt\"; size=3";
                A.Eq("a;b", HeaderValue.GetParameter(cd, "name"));
                A.Eq("x \"q\".txt", HeaderValue.GetParameter(cd, "filename"));
                A.Eq("3", HeaderValue.GetParameter(cd, "SIZE"));
                A.Eq(null, HeaderValue.GetParameter(cd, "missing"));
                A.Eq("multipart/form-data", HeaderValue.GetMediaType("Multipart/Form-Data; boundary=x"));
                A.Eq("na\u00efve.txt", HeaderValue.DecodeExtValue("UTF-8''na%C3%AFve.txt"));
            });

            // ---------------- multipart ----------------

            t.Add("multipart: fields and files", () =>
            {
                byte[] fileData = new byte[5000];
                new Random(1).NextBytes(fileData);
                byte[] body = BuildMultipart("XyZ", Field("title", "Hello \u4e16\u754c"), FilePart("doc", "r\u00e9sum\u00e9.bin", "application/octet-stream", fileData), FilePart("empty", "", "text/plain", new byte[0]), Field("tags", "a"), Field("tags", "b"));
                var form = new HttpValueCollection();
                var files = new HttpFileCollection();
                MultipartParser.Parse(body, 0, body.Length, "XyZ", Encoding.UTF8, form, files, null);
                A.Eq("Hello \u4e16\u754c", form["title"]);
                A.Eq("a,b", form["tags"]);
                A.Eq(2, files.Count);
                HttpPostedFile f = files["doc"];
                A.Eq("r\u00e9sum\u00e9.bin", f.FileName);
                A.Eq(5000, f.ContentLength);
                A.Eq(Convert.ToBase64String(fileData), Convert.ToBase64String(f.ToArray()));
                var ms = new MemoryStream();
                f.InputStream.CopyTo(ms);
                A.Eq(5000L, ms.Length, "stream length");
                A.Eq(0, files["empty"].ContentLength, "empty file");
                A.Eq("", files["empty"].FileName);
            });

            t.Add("multipart: preamble, filename*, no headers part", () =>
            {
                string raw = "preamble text\r\n--B\r\nContent-Disposition: form-data; name=\"f\"; filename=\"fallback.txt\"; filename*=UTF-8''%E2%82%AC.txt\r\n\r\nDATA\r\n--B\r\n\r\nno headers\r\n--B--\r\nepilogue";
                byte[] body = Encoding.UTF8.GetBytes(raw);
                var form = new HttpValueCollection();
                var files = new HttpFileCollection();
                MultipartParser.Parse(body, 0, body.Length, "B", Encoding.UTF8, form, files, null);
                A.Eq("\u20ac.txt", files["f"].FileName);
                A.Eq("DATA", Encoding.UTF8.GetString(files["f"].ToArray()));
                A.Eq(0, form.Count, "part without name is skipped");
            });

            t.Add("multipart: malformed bodies give 400", () =>
            {
                MultipartErr("--B\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nvalue-without-end", "B", "truncated");
                MultipartErr("nothing here", "B", "no boundary");
                MultipartErr("--B\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nv\r\n--B--", "", "empty boundary");
                MultipartErr("--BX\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nv\r\n--B--", "B", "garbage after delimiter");
                MultipartErr("--B\r\nBadHeaderLine\r\n\r\nv\r\n--B--", "B", "bad part header");
                var sb = new StringBuilder();
                for (int i = 0; i < 5; i++) sb.Append("--B\r\nContent-Disposition: form-data; name=\"k" + i + "\"\r\n\r\nv\r\n");
                sb.Append("--B--");
                byte[] b = L(sb.ToString());
                var ex = A.Throws<HttpException>(() => MultipartParser.Parse(b, 0, b.Length, "B", Encoding.UTF8, new HttpValueCollection(), new HttpFileCollection(), new RequestSettings { MaxMultipartParts = 3 }));
                A.Eq(400, ex.GetHttpCode());
            });

            // ---------------- WebApp (HttpUtility) ----------------

            t.Add("WebApp: html encoding matches System.Web", () =>
            {
                A.Eq("&lt;a href=&#39;x&#39;&gt;&amp;&quot;&#233;\u4e16", WebApp.HtmlEncode("<a href='x'>&\"\u00e9\u4e16"));
                A.Eq("&lt;b>&amp;&quot;&#39;", WebApp.HtmlAttributeEncode("<b>&\"'"));
                A.Eq(null, WebApp.HtmlEncode((string)null));
                A.Eq("", WebApp.HtmlEncode((object)null));
                A.Eq("<a>&\"'\u00e9 \u00a0\U0001F600 &bogus; &#xZZ;", WebApp.HtmlDecode("&lt;a&gt;&amp;&quot;&#39;&#233; &nbsp;&#x1F600; &bogus; &#xZZ;"));
            });

            t.Add("WebApp: url and javascript encoding", () =>
            {
                A.Eq("a+b%26c%3dd%2f%c3%a9-_.!*()", WebApp.UrlEncode("a b&c=d/\u00e9-_.!*()"));
                A.Eq("a b&c=d/\u00e9", WebApp.UrlDecode("a+b%26c%3dd%2f%c3%a9"));
                A.Eq("/a%20b/%c3%a9?x=y z", WebApp.UrlPathEncode("/a b/\u00e9?x=y z"));
                A.Eq("\\u003c/script\\u003e\\\"\\u0027\\n\\\\", WebApp.JavaScriptStringEncode("</script>\"'\n\\"));
                A.Eq("\"x\"", WebApp.JavaScriptStringEncode("x", true));
                NameValueCollection q = WebApp.ParseQueryString("?a=1&b=x+y&a=2");
                A.Eq("1,2", q["a"]);
                A.Eq("a=1&a=2&b=x+y", q.ToString());
            });

            // ---------------- HttpRequest ----------------

            t.Add("request: path, query, headers, url", () =>
            {
                var r = MakeRequest("GET /files/a%20b+c/%E4%BD%A0?x=1&x=2&y=%2B HTTP/1.1\r\nHost: example.com:8080\r\nUser-Agent: UA\r\nReferer: http://r.example/\r\nAccept: text/html, */*\r\nCookie: sid=1\r\n\r\n");
                A.Eq("/files/a b+c/\u4f60", r.Path);
                A.Eq("/files/a%20b+c/%E4%BD%A0?x=1&x=2&y=%2B", r.RawUrl);
                A.Eq("1,2", r.QueryString["x"]);
                A.Eq("+", r.QueryString["y"]);
                A.Eq("UA", r.UserAgent);
                A.Eq("UA", r.Headers["user-agent"]);
                A.Eq("http://example.com:8080/files/a%20b+c/%E4%BD%A0?x=1&x=2&y=%2B", r.Url.AbsoluteUri);
                A.Eq("r.example", r.UrlReferrer.Host);
                A.Eq(2, r.AcceptTypes.Length);
                A.Eq("1", r.Cookies["sid"].Value);
                A.Eq("GET", r.HttpMethod);
                A.Eq("example.com", r.ServerVariables["SERVER_NAME"]);
                A.Eq("UA", r.ServerVariables["HTTP_USER_AGENT"]);
                A.True(r.IsLocal, "default connection is loopback");
                A.Throws<NotSupportedException>(() => r.QueryString.Add("z", "1"), "query is read-only");
            });

            t.Add("request: form, indexer order and Unvalidated", () =>
            {
                byte[] body = Encoding.UTF8.GetBytes("a=form&b=2&pwd=%3Cscript%3E");
                var r = MakeRequest("POST /x?a=query HTTP/1.1\r\nHost: h\r\nContent-Type: application/x-www-form-urlencoded; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nCookie: c=cookie\r\n\r\n", body);
                A.Eq("form", r.Form["a"]);
                A.Eq("query", r["a"], "QueryString wins");
                A.Eq("2", r["b"]);
                A.Eq("cookie", r["c"]);
                A.Eq("POST", r["REQUEST_METHOD"], "server variables last");
                A.Eq("<script>", r.Unvalidated.Form["pwd"]);
                A.Eq("query,form", r.Params["a"]);
                A.Eq(body.Length, r.ContentLength);
                var ms = new MemoryStream();
                r.InputStream.CopyTo(ms);
                A.Eq(body.Length, (int)ms.Length);
                r.InputStream.Position = 0;
                A.Eq(5, r.BinaryRead(5).Length);
            });

            t.Add("request: form ignored for other content types", () =>
            {
                byte[] body = Encoding.UTF8.GetBytes("{\"a\":1}");
                var r = MakeRequest("POST /x HTTP/1.1\r\nHost: h\r\nContent-Type: application/json\r\nContent-Length: 7\r\n\r\n", body);
                A.Eq(0, r.Form.Count);
                A.Eq(0, r.Files.Count);
                A.Eq("{\"a\":1}", r.BodyText);
                A.Eq(body.Length, r.BodyBytes.Length);
                A.Eq("{\"a\":1}", Encoding.UTF8.GetString(r.BodyBytes));
            });

            t.Add("request: malformed multipart throws 400 on every access", () =>
            {
                byte[] body = L("--B\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\ntruncated");
                var r = MakeRequest("POST /x HTTP/1.1\r\nHost: h\r\nContent-Type: multipart/form-data; boundary=B\r\nContent-Length: " + body.Length + "\r\n\r\n", body);
                A.Eq(400, A.Throws<HttpException>(() => { var x = r.Form; }).GetHttpCode());
                A.Eq(400, A.Throws<HttpException>(() => { var x = r.Files; }).GetHttpCode(), "second access");
            });

            // ---------------- HttpResponse ----------------

            t.Add("response: buffered gets Content-Length and defaults", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Write("hi ");
                res.Write('x');
                res.Output.Write(42);
                A.True(res.Complete(), "keep-alive");
                string head = sink.HeadText;
                A.True(head.StartsWith("HTTP/1.1 200 OK\r\n"), "status line");
                A.Contains(head, "Content-Length: 6\r\n");
                A.Contains(head, "Content-Type: text/html; charset=utf-8\r\n");
                A.Contains(head, "Cache-Control: private\r\n");
                A.Contains(head, "Date: ");
                A.NotContains(head, "Transfer-Encoding");
                A.NotContains(head, "Server:");
                A.Eq("hi x42", sink.Body);
            });

            t.Add("response: Flush switches to chunked with terminator", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Write("ab");
                res.Flush();
                res.Write("cde");
                res.Complete();
                A.Contains(sink.HeadText, "Transfer-Encoding: chunked");
                A.Eq("2\r\nab\r\n3\r\ncde\r\n0\r\n\r\n", sink.Body);
            });

            t.Add("response: Hearth EndResponse pattern (Flush + SuppressContent + CompleteRequest)", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                var ctx = new HttpContext(MakeRequest("GET / HTTP/1.1\r\nHost: h\r\n\r\n"), res);
                res.ContentType = "application/json";
                res.Write("{\"success\":true}");
                try { res.Flush(); } catch { }
                res.SuppressContent = true;
                ctx.ApplicationInstance.CompleteRequest();
                res.Write("ignored");
                A.True(res.Complete(), "keep-alive");
                A.True(ctx.IsRequestCompleted, "completed flag");
                A.Contains(sink.HeadText, "Content-Type: application/json; charset=utf-8");
                A.Eq("10\r\n{\"success\":true}\r\n0\r\n\r\n", sink.Body);
            });

            t.Add("response: HEAD has length but no body", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink, "HEAD / HTTP/1.1\r\nHost: h\r\n\r\n");
                res.Write("hello");
                res.Complete();
                A.Contains(sink.HeadText, "Content-Length: 5");
                A.Eq("", sink.Body);
            });

            t.Add("response: 204 and 304 carry no body or framing", () =>
            {
                foreach (int code in new[] { 204, 304 })
                {
                    MemorySink sink;
                    var res = MakeResponse(out sink);
                    res.StatusCode = code;
                    res.Write("should not appear");
                    res.Complete();
                    A.NotContains(sink.Text, "Content-Length", code.ToString());
                    A.NotContains(sink.Text, "Transfer-Encoding", code.ToString());
                    A.NotContains(sink.Text, "Content-Type", code.ToString());
                    A.Eq("", sink.Body, code.ToString());
                }
            });

            t.Add("response: SuppressContent sends Content-Length 0", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Write("secret");
                res.SuppressContent = true;
                res.Complete();
                A.Contains(sink.HeadText, "Content-Length: 0");
                A.Eq("", sink.Body);
            });

            t.Add("response: redirect", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Write("discarded");
                res.Redirect("~/login?x=\"1\"", false);
                res.Complete();
                A.True(sink.Text.StartsWith("HTTP/1.1 302 Found\r\n"), "302");
                A.Contains(sink.HeadText, "Location: /login?x=\"1\"\r\n");
                A.NotContains(sink.Body, "discarded");
                A.Contains(sink.Body, "href=\"/login?x=&quot;1&quot;\"");
                A.Throws<ArgumentException>(() => MakeResponse(out sink).Redirect("/x\r\nSet-Cookie: a=b", false));
            });

            t.Add("response: End stops writes even if swallowed", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Write("a");
                try { res.End(); } catch (Exception) { }
                res.Write("b");
                res.Complete();
                A.Eq("a", sink.Body);
                A.Contains(sink.HeadText, "Content-Length: 1");
            });

            t.Add("response: changes after headers are sent throw", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Flush();
                A.Throws<HttpException>(() => res.StatusCode = 500);
                A.Throws<HttpException>(() => res.AppendHeader("X", "1"));
                A.Throws<HttpException>(() => res.ContentType = "text/plain");
                A.Throws<HttpException>(() => res.Redirect("/x"));
            });

            t.Add("response: header injection is refused", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                A.Throws<ArgumentException>(() => res.AppendHeader("X-A", "1\r\nSet-Cookie: evil=1"));
                A.Throws<ArgumentException>(() => res.AppendHeader("Bad Name", "1"));
                res.Cookies.Add(new HttpCookie("a", "1\r\nX: y"));
                A.Throws<ArgumentException>(() => res.Complete());
                A.Eq(0L, sink.Stream.Length, "nothing was sent");
            });

            t.Add("response: cookies, custom headers, framing headers ignored", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Cookies.Add(new HttpCookie("a", "1") { HttpOnly = true });
                res.Cookies.Add(new HttpCookie("b", "2"));
                res.AppendHeader("X-Frame-Options", "DENY");
                res.AppendHeader("Transfer-Encoding", "gzip");
                res.Write("x");
                res.Complete();
                string head = sink.HeadText;
                A.Contains(head, "Set-Cookie: a=1; path=/; HttpOnly\r\n");
                A.Contains(head, "Set-Cookie: b=2; path=/\r\n");
                A.Contains(head, "X-Frame-Options: DENY\r\n");
                A.NotContains(head, "gzip");
            });

            t.Add("response: HTTP/1.0 and Connection: close", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink, "GET / HTTP/1.0\r\n\r\n");
                res.BufferOutput = false;
                res.Write("streamed");
                A.False(res.Complete(), "must close");
                A.Contains(sink.HeadText, "Connection: close");
                A.NotContains(sink.HeadText, "Transfer-Encoding");
                A.Eq("streamed", sink.Body);

                res = MakeResponse(out sink, "GET / HTTP/1.0\r\nConnection: keep-alive\r\n\r\n");
                res.Write("x");
                A.True(res.Complete(), "1.0 keep-alive with length");
                A.Contains(sink.HeadText, "Connection: keep-alive");

                res = MakeResponse(out sink, "GET / HTTP/1.1\r\nHost: h\r\nConnection: close\r\n\r\n");
                res.Write("x");
                A.False(res.Complete(), "client asked to close");
                A.Contains(sink.HeadText, "Connection: close");
            });

            t.Add("response: unicode, surrogate pairs split across writes", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                string emoji = "\U0001F600";
                res.Write("\u4f60\u597d");
                res.Write(emoji[0]);
                res.Write(emoji[1]);
                res.Complete();
                byte[] all = sink.Stream.ToArray();
                string text = Encoding.UTF8.GetString(all);
                A.True(text.EndsWith("\u4f60\u597d" + emoji), "utf-8 body");
                A.Contains(sink.HeadText, "Content-Length: 10");
            });

            t.Add("response: TransmitFile streams file, ranges and HEAD", () =>
            {
                string path = Path.Combine(Path.GetTempPath(), "cshttp-tf-" + Guid.NewGuid().ToString("N") + ".txt");
                try
                {
                    File.WriteAllText(path, "0123456789");
                    MemorySink sink;
                    var res = MakeResponse(out sink);
                    res.Write("[");
                    res.TransmitFile(path, 2, 3);
                    res.Write("]");
                    res.Complete();
                    A.Eq("[234]", sink.Body);
                    A.Contains(sink.HeadText, "Content-Length: 5");

                    res = MakeResponse(out sink, "HEAD / HTTP/1.1\r\nHost: h\r\n\r\n");
                    A.True(res.TransmitFile(path), "head transmit");
                    res.Complete();
                    A.Contains(sink.HeadText, "Content-Length: 10");
                    A.Eq("", sink.Body);

                    res = MakeResponse(out sink);
                    res.BufferOutput = false;
                    A.True(res.TransmitFile(path), "stream transmit");
                    res.Complete();
                    A.Eq("a\r\n0123456789\r\n0\r\n\r\n", sink.Body);

                    A.False(MakeResponse(out sink).TransmitFile(path + ".missing"), "missing file returns false");
                    A.Throws<FileNotFoundException>(() => MakeResponse(out sink).TransmitFile(path + ".missing", 0, -1));
                }
                finally { File.Delete(path); }
            });

            t.Add("response: wrong user Content-Length forces close", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.AppendHeader("Content-Length", "3");
                res.BufferOutput = false;
                res.Write("abcdef");
                A.False(res.Complete(), "overrun must close");
                A.Eq("abc", sink.Body, "truncated to declared length");

                res = MakeResponse(out sink);
                res.AppendHeader("Content-Length", "10");
                res.Flush();
                res.Write("ab");
                A.False(res.Complete(), "underrun must close");
            });

            t.Add("response: Reset for error pages", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Write("partial");
                res.Cookies.Add(new HttpCookie("a", "1"));
                res.Reset();
                res.StatusCode = 500;
                res.Write("error");
                res.Complete();
                A.True(sink.Text.StartsWith("HTTP/1.1 500"), "500");
                A.NotContains(sink.Text, "Set-Cookie");
                A.Eq("error", sink.Body);
            });

            t.Add("response: Cache policy (System.Web Response.Cache)", () =>
            {
                MemorySink sink;
                var res = MakeResponse(out sink);
                res.Cache.SetCacheability(HttpCacheability.Private);
                res.Cache.SetMaxAge(TimeSpan.FromDays(7));
                res.Write("x");
                res.Complete();
                A.Contains(sink.HeadText, "Cache-Control: private, max-age=604800\r\n");

                res = MakeResponse(out sink);
                res.Cache.SetCacheability(HttpCacheability.NoCache);
                res.Cache.SetNoStore();
                res.Cache.SetExpires(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                res.Complete();
                A.Contains(sink.HeadText, "Cache-Control: no-cache, no-store\r\n");
                A.Contains(sink.HeadText, "Expires: Sat, 01 Jan 2000 00:00:00 GMT\r\n");

                res = MakeResponse(out sink);
                res.Cache.SetMaxAge(TimeSpan.FromMinutes(1));
                res.Complete();
                A.Contains(sink.HeadText, "Cache-Control: private, max-age=60\r\n", "keeps default cacheability");
            });
            // ---------------- hosting ----------------

            t.Add("hosting: MapPath stays inside the root", () =>
            {
                string root = WebApp.ApplicationPhysicalPath;
                A.Eq(Path.Combine(root, "App_Data"), WebApp.MapPath("~/App_Data"));
                A.Eq(Path.Combine(root, "css", "site.css"), WebApp.MapPath("/css/site.css"));
                A.Eq(root.TrimEnd(Path.DirectorySeparatorChar), WebApp.MapPath("~/"));
                A.Throws<HttpException>(() => WebApp.MapPath("~/../outside"));
                A.Throws<HttpException>(() => WebApp.MapPath("/a/../../outside"));
                A.Throws<ArgumentException>(() => WebApp.MapPath("C:\\Windows"));
            });

            t.Add("hosting: background work completes and shutdown waits", () =>
            {
                WebApp.BeginHosting(null);
                int done = 0;
                var started = new ManualResetEventSlim();
                WebApp.BackgroundWork(ct => { started.Set(); Thread.Sleep(200); Interlocked.Increment(ref done); });
                WebApp.BackgroundWork(async ct => { await System.Threading.Tasks.Task.Delay(50); Interlocked.Increment(ref done); });
                WebApp.BackgroundWork(ct => { throw new InvalidOperationException("reported, not crashing"); });
                started.Wait(2000);
                A.True(WebApp.InitiateShutdown(TimeSpan.FromSeconds(5)), "all finished");
                A.Eq(2, done);
                WebApp.BackgroundWork(ct => Interlocked.Increment(ref done));
                Thread.Sleep(50);
                A.Eq(2, done, "ignored after shutdown");
                WebApp.BeginHosting(null);
            });

            t.Add("static: range parsing", () =>
            {
                long s, e;
                A.Eq(206, StaticFiles.ParseRange("bytes=0-4", 10, out s, out e)); A.Eq(0L, s); A.Eq(4L, e);
                A.Eq(206, StaticFiles.ParseRange("bytes=5-", 10, out s, out e)); A.Eq(5L, s); A.Eq(9L, e);
                A.Eq(206, StaticFiles.ParseRange("bytes=-3", 10, out s, out e)); A.Eq(7L, s); A.Eq(9L, e);
                A.Eq(206, StaticFiles.ParseRange("bytes=-30", 10, out s, out e)); A.Eq(0L, s);
                A.Eq(206, StaticFiles.ParseRange("bytes=8-100", 10, out s, out e)); A.Eq(9L, e);
                A.Eq(416, StaticFiles.ParseRange("bytes=10-", 10, out s, out e));
                A.Eq(416, StaticFiles.ParseRange("bytes=-0", 10, out s, out e));
                A.Eq(200, StaticFiles.ParseRange("bytes=0-1,3-4", 10, out s, out e));
                A.Eq(200, StaticFiles.ParseRange("items=0-1", 10, out s, out e));
                A.Eq(200, StaticFiles.ParseRange("bytes=5-2", 10, out s, out e));
                A.Eq(200, StaticFiles.ParseRange("bytes=a-b", 10, out s, out e));
            });
        }

        static void MultipartErr(string raw, string boundary, string what)
        {
            byte[] b = L(raw);
            var ex = A.Throws<HttpException>(() => MultipartParser.Parse(b, 0, b.Length, boundary, Encoding.UTF8, new HttpValueCollection(), new HttpFileCollection(), null), what);
            A.Eq(400, ex.GetHttpCode(), what);
        }
    }
}
