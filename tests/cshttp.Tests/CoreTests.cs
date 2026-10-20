using System;
using System.IO;
using System.Text;
using static cshttp.Tests.Bytes;

namespace cshttp.Tests
{
    static class CoreTests
    {
        static HttpParseResult Parse(string raw, out HttpRequestHead head, ParserOptions o = null)
        {
            byte[] b = L(raw);
            return HttpRequestParser.ParseHead(b, 0, b.Length, o, out head);
        }

        static HttpRequestHead Ok(string raw, ParserOptions o = null)
        {
            HttpRequestHead h;
            var r = Parse(raw, out h, o);
            A.Status(r, 0, "parse");
            A.Eq(L(raw).Length, r.Consumed, "consumed");
            return h;
        }

        static void Err(string raw, int status, string what, ParserOptions o = null)
        {
            HttpRequestHead h;
            A.Status(Parse(raw, out h, o), status, what);
        }

        public static void Register(TestList t)
        {
            t.Add("parser: simple GET", () =>
            {
                var h = Ok("GET /a/b%20c?x=1&y=2 HTTP/1.1\r\nHost: example.com\r\nUser-Agent: t\r\n\r\n");
                A.Eq("GET", h.Method);
                A.Eq("/a/b%20c", h.Path);
                A.Eq("x=1&y=2", h.Query);
                A.Eq("example.com", h.Host);
                A.Eq(1, h.VersionMinor);
                A.True(h.KeepAlive, "keep-alive default for 1.1");
                A.Eq(HttpBodyKind.None, h.BodyKind);
                A.Eq("t", h.Headers.Get("user-agent"));
            });

            t.Add("parser: every prefix is NeedMoreData", () =>
            {
                byte[] b = L("POST /x HTTP/1.1\r\nHost: h\r\nContent-Length: 3\r\nX-A:  v  \r\n\r\n");
                for (int i = 0; i < b.Length; i++)
                {
                    HttpRequestHead h;
                    A.Status(HttpRequestParser.ParseHead(b, 0, i, null, out h), -1, "prefix " + i);
                }
                HttpRequestHead full;
                A.Status(HttpRequestParser.ParseHead(b, 0, b.Length, null, out full), 0, "full");
                A.Eq("v", full.Headers.Get("X-A"), "trimmed value");
                A.Eq(3L, full.ContentLength);
            });

            t.Add("parser: offset inside a larger buffer", () =>
            {
                byte[] b = L("JUNKGET / HTTP/1.1\r\nHost: h\r\n\r\nTAIL");
                HttpRequestHead h;
                var r = HttpRequestParser.ParseHead(b, 4, b.Length - 4, null, out h);
                A.Status(r, 0, "parse");
                A.Eq(b.Length - 8, r.Consumed);
            });

            t.Add("parser: leading empty lines ignored", () => Ok("\r\n\r\nGET / HTTP/1.1\r\nHost: h\r\n\r\n"));

            t.Add("parser: bare LF rejected by default, allowed by option", () =>
            {
                Err("GET / HTTP/1.1\nHost: h\n\n", 400, "bare LF");
                Err("GET / HTTP/1.1\r\nHost: h\n\r\n", 400, "bare LF in header");
                var h = Ok("GET / HTTP/1.1\nHost: h\n\n", new ParserOptions { AllowBareLf = true });
                A.Eq("h", h.Host);
            });

            t.Add("parser: bare CR rejected", () =>
            {
                Err("GET / HTTP/1.1\r\nHost: h\r\nX: a\rb\r\n\r\n", 400, "CR in value");
                Err("GET / HTTP/1.1\r\nHost: h\r\n\rX: 1\r\n\r\n", 400, "CR at line start");
            });

            t.Add("parser: obs-fold and whitespace before colon rejected", () =>
            {
                Err("GET / HTTP/1.1\r\nHost: h\r\nX: a\r\n b\r\n\r\n", 400, "obs-fold");
                Err("GET / HTTP/1.1\r\nHost: h\r\nX : a\r\n\r\n", 400, "space before colon");
                Err("GET / HTTP/1.1\r\nHost: h\r\n: a\r\n\r\n", 400, "empty name");
                Err("GET / HTTP/1.1\r\nHost: h\r\nNoColon\r\n\r\n", 400, "no colon");
            });

            t.Add("parser: control chars in values rejected, obs-text kept", () =>
            {
                Err("GET / HTTP/1.1\r\nHost: h\r\nX: a\0b\r\n\r\n", 400, "NUL");
                Err("GET / HTTP/1.1\r\nHost: h\r\nX: a\u0001b\r\n\r\n", 400, "SOH");
                var h = Ok("GET / HTTP/1.1\r\nHost: h\r\nX: caf\u00e9\tz\r\n\r\n");
                A.Eq("caf\u00e9\tz", h.Headers.Get("X"));
            });

            t.Add("parser: Host rules", () =>
            {
                Err("GET / HTTP/1.1\r\n\r\n", 400, "missing host 1.1");
                Err("GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n", 400, "duplicate host");
                Err("GET / HTTP/1.1\r\nHost: a/b\r\n\r\n", 400, "slash in host");
                Err("GET / HTTP/1.1\r\nHost: a@b\r\n\r\n", 400, "@ in host");
                var h = Ok("GET / HTTP/1.0\r\n\r\n");
                A.False(h.KeepAlive, "1.0 closes by default");
                h = Ok("GET / HTTP/1.0\r\nConnection: keep-alive\r\n\r\n");
                A.True(h.KeepAlive, "1.0 keep-alive");
                h = Ok("GET / HTTP/1.1\r\nHost: [::1]:8080\r\n\r\n");
                A.Eq("[::1]:8080", h.Host);
            });

            t.Add("parser: smuggling - Content-Length with Transfer-Encoding", () =>
            {
                Err("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\nTransfer-Encoding: chunked\r\n\r\n", 400, "CL+TE");
                Err("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\nContent-Length: 5\r\n\r\n", 400, "TE+CL");
            });

            t.Add("parser: smuggling - Content-Length variants", () =>
            {
                Err("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\n", 400, "conflict");
                Err("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5, 6\r\n\r\n", 400, "conflict list");
                Err("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: +5\r\n\r\n", 400, "plus");
                Err("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: -1\r\n\r\n", 400, "negative");
                Err("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5a\r\n\r\n", 400, "suffix");
                Err("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: \r\n\r\n", 400, "empty");
                Err("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 99999999999999999999\r\n\r\n", 400, "overflow");
                var h = Ok("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\nContent-Length: 5\r\n\r\n");
                A.Eq(5L, h.ContentLength);
                h = Ok("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5, 5\r\n\r\n");
                A.Eq(5L, h.ContentLength);
                h = Ok("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 0\r\n\r\n");
                A.Eq(HttpBodyKind.None, h.BodyKind);
            });

            t.Add("parser: smuggling - Transfer-Encoding variants", () =>
            {
                var h = Ok("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: Chunked\r\n\r\n");
                A.Eq(HttpBodyKind.Chunked, h.BodyKind);
                Err("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: gzip, chunked\r\n\r\n", 501, "gzip,chunked");
                Err("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked, gzip\r\n\r\n", 400, "chunked not last");
                Err("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked, chunked\r\n\r\n", 400, "chunked twice");
                Err("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\nTransfer-Encoding: chunked\r\n\r\n", 400, "chunked twice headers");
                Err("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: xchunked\r\n\r\n", 400, "xchunked");
                Err("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: \r\n\r\n", 400, "empty TE");
                Err("POST / HTTP/1.0\r\nTransfer-Encoding: chunked\r\n\r\n", 400, "TE in 1.0");
            });

            t.Add("parser: versions", () =>
            {
                Err("GET / HTTP/2.0\r\nHost: h\r\n\r\n", 505, "2.0");
                Err("GET / http/1.1\r\nHost: h\r\n\r\n", 400, "lower case");
                Err("GET / HTTP/1.1x\r\nHost: h\r\n\r\n", 400, "suffix");
                Err("GET / HTTP/1\r\nHost: h\r\n\r\n", 400, "short");
                var h = Ok("GET / HTTP/1.2\r\nHost: h\r\n\r\n");
                A.Eq(1, h.VersionMinor, "1.2 treated as 1.1");
            });

            t.Add("parser: request line shape", () =>
            {
                Err("GET  / HTTP/1.1\r\nHost: h\r\n\r\n", 400, "double space");
                Err("GET / HTTP/1.1 \r\nHost: h\r\n\r\n", 400, "trailing space");
                Err("GE(T / HTTP/1.1\r\nHost: h\r\n\r\n", 400, "bad method char");
                Err("GET /a\u007fb HTTP/1.1\r\nHost: h\r\n\r\n", 400, "DEL in target");
                Err("GET /a#frag HTTP/1.1\r\nHost: h\r\n\r\n", 400, "fragment");
                Err("GET a/b HTTP/1.1\r\nHost: h\r\n\r\n", 400, "relative target");
                Err(" GET / HTTP/1.1\r\nHost: h\r\n\r\n", 400, "leading space");
                Err("GET\r\n\r\n", 400, "method only");
                var h = Ok("PROPFIND /x HTTP/1.1\r\nHost: h\r\n\r\n");
                A.Eq("PROPFIND", h.Method, "extension method");
            });

            t.Add("parser: target forms", () =>
            {
                var h = Ok("GET http://example.com:8080/a?b=1 HTTP/1.1\r\nHost: other\r\n\r\n");
                A.Eq(RequestTargetForm.Absolute, h.TargetForm);
                A.Eq("example.com:8080", h.Host);
                A.Eq("/a", h.Path);
                A.Eq("b=1", h.Query);
                h = Ok("GET http://example.com HTTP/1.1\r\nHost: example.com\r\n\r\n");
                A.Eq("/", h.Path);
                h = Ok("OPTIONS * HTTP/1.1\r\nHost: h\r\n\r\n");
                A.Eq(RequestTargetForm.Asterisk, h.TargetForm);
                Err("GET * HTTP/1.1\r\nHost: h\r\n\r\n", 400, "* with GET");
                Err("CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n", 501, "CONNECT");
                Err("GET http://user@evil/ HTTP/1.1\r\nHost: h\r\n\r\n", 400, "userinfo");
                h = Ok("GET /p? HTTP/1.1\r\nHost: h\r\n\r\n");
                A.Eq("", h.Query, "empty query");
                h = Ok("GET /p HTTP/1.1\r\nHost: h\r\n\r\n");
                A.Eq(null, h.Query, "no query");
            });

            t.Add("parser: limits 414 / 431", () =>
            {
                var o = new ParserOptions { MaxRequestLineLength = 100, MaxHeaderBytes = 400, MaxHeaderCount = 5 };
                string longTarget = "/" + new string('a', 200);
                Err("GET " + longTarget + " HTTP/1.1\r\nHost: h\r\n\r\n", 414, "long line complete", o);
                Err("GET " + longTarget, 414, "long line incomplete", o);
                Err("GET / HTTP/1.1\r\nHost: h\r\nX: " + new string('b', 500) + "\r\n\r\n", 431, "big header", o);
                Err("GET / HTTP/1.1\r\nHost: h\r\nX: " + new string('b', 500), 431, "big header incomplete", o);
                Err("GET / HTTP/1.1\r\nHost: h\r\nA: 1\r\nB: 1\r\nC: 1\r\nD: 1\r\nE: 1\r\n\r\n", 431, "too many", o);
                Ok("GET / HTTP/1.1\r\nHost: h\r\nA: 1\r\nB: 1\r\nC: 1\r\nD: 1\r\n\r\n", o);
            });

            t.Add("parser: Expect and Connection", () =>
            {
                var h = Ok("POST / HTTP/1.1\r\nHost: h\r\nExpect: 100-continue\r\nContent-Length: 1\r\n\r\n");
                A.True(h.ExpectContinue, "expect");
                Err("POST / HTTP/1.1\r\nHost: h\r\nExpect: something\r\n\r\n", 417, "unknown expectation");
                h = Ok("GET / HTTP/1.1\r\nHost: h\r\nConnection: Upgrade, close\r\n\r\n");
                A.False(h.KeepAlive, "close token");
                h = Ok("GET / HTTP/1.1\r\nHost: h\r\nConnection: keep-alive\r\n\r\n");
                A.True(h.KeepAlive, "keep-alive");
            });

            t.Add("parser: pipelined requests in one buffer", () =>
            {
                byte[] b = L("GET /1 HTTP/1.1\r\nHost: h\r\n\r\nGET /2 HTTP/1.1\r\nHost: h\r\n\r\nGET /3 HTTP/1.1\r\nHost:");
                int pos = 0;
                HttpRequestHead h;
                var r = HttpRequestParser.ParseHead(b, pos, b.Length - pos, null, out h);
                A.Eq("/1", h.Path);
                pos += r.Consumed;
                r = HttpRequestParser.ParseHead(b, pos, b.Length - pos, null, out h);
                A.Eq("/2", h.Path);
                pos += r.Consumed;
                A.Status(HttpRequestParser.ParseHead(b, pos, b.Length - pos, null, out h), -1, "third incomplete");
            });

            t.Add("parser: duplicate headers preserved in order", () =>
            {
                var h = Ok("GET / HTTP/1.1\r\nHost: h\r\nX: 1\r\nx: 2\r\nCookie: a=1\r\nCookie: b=2\r\n\r\n");
                A.Eq("1, 2", h.Headers.GetCombined("X"));
                A.Eq(2, h.Headers.GetAll("cookie").Count);
            });

            // ---------------- chunked ----------------

            t.Add("chunked: basic with extension and trailer", () =>
            {
                string body = DecodeAll(L("5;ext=1\r\nhello\r\n6\r\n world\r\n0\r\nX-T: 1\r\n\r\nNEXT"), 1 << 20, out int consumed);
                A.Eq("hello world", body);
                A.Eq(L("5;ext=1\r\nhello\r\n6\r\n world\r\n0\r\nX-T: 1\r\n\r\n").Length, consumed, "stops before next message");
            });

            t.Add("chunked: byte-by-byte equals whole", () =>
            {
                byte[] input = L("a\r\n0123456789\r\nA\r\nabcdefghij\r\n0\r\n\r\n");
                for (int step = 1; step <= 7; step++)
                {
                    string got = DecodeAll(input, step, out int consumed);
                    A.Eq("0123456789abcdefghij", got, "step " + step);
                    A.Eq(input.Length, consumed, "consumed step " + step);
                }
            });

            t.Add("chunked: malformed inputs", () =>
            {
                ChunkErr("x\r\n");
                ChunkErr("\r\n");
                ChunkErr("5\r\nhelloXX");
                ChunkErr("5\nhello\r\n0\r\n\r\n");
                ChunkErr("5\r\nhello\r\n0\r\n\rX");
                ChunkErr("FFFFFFFFFFFFFFFFF\r\n");
                ChunkErr("5 x\r\n");
                ChunkErr("5;a\x01\r\n");
                ChunkErr("0\r\nBad Trailer\x01\r\n\r\n");
                ChunkErr("-5\r\n");
            });

            t.Add("chunked: limits on line and trailers", () =>
            {
                var d = new ChunkedDecoder { MaxLineLength = 10 };
                byte[] b = L("5;" + new string('a', 50) + "\r\n");
                int c, o, n;
                A.Eq(ChunkedStatus.Error, d.Decode(b, 0, b.Length, out c, out o, out n));
                d = new ChunkedDecoder { MaxTrailerBytes = 10 };
                b = L("0\r\nX-Long: " + new string('a', 50) + "\r\n\r\n");
                A.Eq(ChunkedStatus.Error, d.Decode(b, 0, b.Length, out c, out o, out n));
            });

            t.Add("chunked: random round trip", () =>
            {
                var rnd = new Random(1234);
                for (int iter = 0; iter < 200; iter++)
                {
                    byte[] data = new byte[rnd.Next(0, 5000)];
                    rnd.NextBytes(data);
                    var ms = new MemoryStream();
                    int p = 0;
                    while (p < data.Length)
                    {
                        int n = Math.Min(data.Length - p, rnd.Next(1, 700));
                        HttpResponseWriter.WriteChunk(ms, data, p, n);
                        p += n;
                    }
                    ms.Write(HttpResponseWriter.LastChunk, 0, 5);
                    byte[] enc = ms.ToArray();
                    var dec = new ChunkedDecoder();
                    var outp = new MemoryStream();
                    int pos = 0;
                    bool done = false;
                    while (!done)
                    {
                        int avail = Math.Min(enc.Length - pos, rnd.Next(1, 300));
                        int consumed, off, cnt;
                        var st = dec.Decode(enc, pos, avail, out consumed, out off, out cnt);
                        pos += consumed;
                        if (st == ChunkedStatus.Data) outp.Write(enc, off, cnt);
                        else if (st == ChunkedStatus.Complete) done = true;
                        else if (st == ChunkedStatus.Error) throw new Exception(dec.ErrorMessage);
                    }
                    A.Eq(enc.Length, pos, "consumed");
                    A.Eq(Convert.ToBase64String(data), Convert.ToBase64String(outp.ToArray()), "data iter " + iter);
                }
            });

            // ---------------- response writer ----------------

            t.Add("writer: head build and injection guard", () =>
            {
                var h = new HttpHeaders();
                h.Add("X-A", "1");
                string s = S(HttpResponseWriter.BuildHead(404, null, h));
                A.Eq("HTTP/1.1 404 Not Found\r\nX-A: 1\r\n\r\n", s);
                var bad = new HttpHeaders();
                bad.Add("X", "a\r\nSet-Cookie: evil=1");
                A.Throws<ArgumentException>(() => HttpResponseWriter.BuildHead(200, null, bad));
                var badName = new HttpHeaders();
                badName.Add("X Y", "1");
                A.Throws<ArgumentException>(() => HttpResponseWriter.BuildHead(200, null, badName));
                A.Throws<ArgumentException>(() => HttpResponseWriter.BuildHead(200, "OK\r\nX: 1", null));
            });

            t.Add("writer: dates", () =>
            {
                var d = new DateTime(1994, 11, 6, 8, 49, 37, DateTimeKind.Utc);
                A.Eq("Sun, 06 Nov 1994 08:49:37 GMT", HttpDate.Format(d));
                DateTime p;
                A.True(HttpDate.TryParse("Sun, 06 Nov 1994 08:49:37 GMT", out p) && p == d, "imf");
                A.True(HttpDate.TryParse("Sunday, 06-Nov-94 08:49:37 GMT", out p) && p == d, "rfc850");
                A.True(HttpDate.TryParse("Sun Nov  6 08:49:37 1994", out p) && p == d, "asctime");
                A.False(HttpDate.TryParse("garbage", out p), "garbage");
            });

            t.Add("bytescan: pattern search agrees with naive search", () =>
            {
                var rnd = new Random(5);
                for (int i = 0; i < 2000; i++)
                {
                    byte[] hay = new byte[rnd.Next(0, 300)];
                    for (int k = 0; k < hay.Length; k++) hay[k] = (byte)rnd.Next(0, 3);
                    byte[] pat = new byte[rnd.Next(1, 5)];
                    for (int k = 0; k < pat.Length; k++) pat[k] = (byte)rnd.Next(0, 3);
                    int off = hay.Length == 0 ? 0 : rnd.Next(0, hay.Length);
                    int cnt = hay.Length - off;
                    A.Eq(Naive(hay, off, cnt, pat), ByteScan.IndexOf(hay, off, cnt, pat), "iteration " + i);
                }
            });
        }

        static int Naive(byte[] h, int off, int cnt, byte[] p)
        {
            for (int i = off; i + p.Length <= off + cnt; i++)
            {
                int k = 0;
                while (k < p.Length && h[i + k] == p[k]) k++;
                if (k == p.Length) return i;
            }
            return -1;
        }

        static string DecodeAll(byte[] input, int step, out int consumedTotal)
        {
            var d = new ChunkedDecoder();
            var sb = new StringBuilder();
            int pos = 0;
            while (true)
            {
                int avail = Math.Min(step, input.Length - pos);
                int consumed, off, cnt;
                var st = d.Decode(input, pos, avail, out consumed, out off, out cnt);
                pos += consumed;
                if (st == ChunkedStatus.Data) sb.Append(S(input, off, cnt));
                else if (st == ChunkedStatus.Complete) break;
                else if (st == ChunkedStatus.Error) throw new AssertException("decode error: " + d.ErrorMessage);
                else if (pos >= input.Length) throw new AssertException("ran out of input");
            }
            consumedTotal = pos;
            return sb.ToString();
        }

        static void ChunkErr(string raw)
        {
            byte[] b = L(raw);
            var d = new ChunkedDecoder();
            int pos = 0;
            while (true)
            {
                int consumed, off, cnt;
                var st = d.Decode(b, pos, b.Length - pos, out consumed, out off, out cnt);
                pos += consumed;
                if (st == ChunkedStatus.Error) return;
                if (st == ChunkedStatus.Complete) throw new AssertException("expected chunked error for: " + raw.Replace("\r", "\\r").Replace("\n", "\\n"));
                if (st == ChunkedStatus.NeedMoreData) throw new AssertException("expected chunked error, got NeedMoreData for: " + raw.Replace("\r", "\\r").Replace("\n", "\\n"));
            }
        }
    }
}
