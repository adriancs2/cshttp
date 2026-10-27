using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using static cshttp.Tests.Bytes;

namespace cshttp.Tests
{
    /// <summary>Randomized robustness tests. Seeds are fixed so failures are reproducible.</summary>
    static class FuzzTests
    {
        static readonly string[] Seeds =
        {
            "GET / HTTP/1.1\r\nHost: a\r\n\r\n",
            "GET /path/to?x=1&y=%20 HTTP/1.1\r\nHost: example.com:80\r\nUser-Agent: fuzz\r\nCookie: a=1; b=2\r\nAccept: */*\r\n\r\n",
            "POST /form HTTP/1.1\r\nHost: h\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 7\r\n\r\na=1&b=2",
            "POST /up HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n",
            "HEAD /x HTTP/1.0\r\nConnection: keep-alive\r\n\r\n",
            "OPTIONS * HTTP/1.1\r\nHost: h\r\n\r\n",
            "GET http://proxy.example/abs?q HTTP/1.1\r\nHost: other\r\nExpect: 100-continue\r\n\r\n",
        };

        static readonly string[] Tokens =
        {
            "\r", "\n", "\r\n", " ", "\t", ":", "\0", "%", ",", ";", "\u0080", "\u00ff", "chunked",
            "Content-Length: 5\r\n", "Transfer-Encoding: chunked\r\n", "Host: x\r\n", "HTTP/1.1", "\r\n\r\n", "--", "=", "&"
        };

        static byte[] Mutate(Random rnd, byte[] src)
        {
            var b = new List<byte>(src);
            int ops = rnd.Next(1, 5);
            for (int k = 0; k < ops; k++)
            {
                int op = rnd.Next(6);
                int pos = b.Count == 0 ? 0 : rnd.Next(b.Count);
                switch (op)
                {
                    case 0:
                        if (b.Count > 0) b[pos] = (byte)rnd.Next(256);
                        break;
                    case 1:
                        b.InsertRange(pos, L(Tokens[rnd.Next(Tokens.Length)]));
                        break;
                    case 2:
                        if (b.Count > 0) b.RemoveRange(pos, Math.Min(b.Count - pos, rnd.Next(1, 8)));
                        break;
                    case 3:
                        if (b.Count > 0)
                        {
                            int len = Math.Min(b.Count - pos, rnd.Next(1, 20));
                            b.InsertRange(pos, b.GetRange(pos, len));
                        }
                        break;
                    case 4:
                        if (b.Count > 0) b.RemoveRange(pos, b.Count - pos);
                        break;
                    case 5:
                        b.Insert(pos, (byte)rnd.Next(256));
                        break;
                }
            }
            return b.ToArray();
        }

        static readonly int[] AllowedErrors = { 400, 414, 417, 431, 501, 505 };

        public static void Register(TestList t)
        {
            t.Add("fuzz: request parser never throws, results are consistent", () =>
            {
                var rnd = new Random(20261019);
                int complete = 0, errors = 0;
                for (int i = 0; i < 40000; i++)
                {
                    byte[] input = Mutate(rnd, L(Seeds[rnd.Next(Seeds.Length)]));
                    HttpRequestHead head;
                    HttpParseResult r;
                    try { r = HttpRequestParser.ParseHead(input, 0, input.Length, null, out head); }
                    catch (Exception ex) { throw new AssertException("parser threw " + ex.GetType().Name + " on input #" + i + ": " + Escape(input)); }

                    if (r.Status == HttpParseStatus.Error)
                    {
                        errors++;
                        A.True(Array.IndexOf(AllowedErrors, r.ErrorStatusCode) >= 0, "unexpected status " + r.ErrorStatusCode);
                    }
                    else if (r.Status == HttpParseStatus.Complete)
                    {
                        complete++;
                        A.True(r.Consumed > 0 && r.Consumed <= input.Length, "consumed in range");
                        A.True(head != null && head.Headers != null, "head populated");
                        if (head.VersionMinor >= 1) A.Eq(1, head.Headers.CountOf("Host") + (head.TargetForm == RequestTargetForm.Absolute ? 1 : 0) > 0 ? 1 : 0, "1.1 has host");
                        A.False(head.BodyKind == HttpBodyKind.Chunked && head.Headers.Contains("Content-Length"), "never CL+TE");
                        // Every strict prefix of a complete head must be NeedMoreData.
                        int p = rnd.Next(0, r.Consumed);
                        HttpRequestHead h2;
                        var pr = HttpRequestParser.ParseHead(input, 0, p, null, out h2);
                        if (pr.Status != HttpParseStatus.NeedMoreData)
                            throw new AssertException("prefix " + p + "/" + r.Consumed + " gave " + pr + " for " + Escape(input));
                    }
                }
                A.True(complete > 1000 && errors > 1000, "mutations exercise both paths: complete=" + complete + " errors=" + errors);
            });

            t.Add("fuzz: chunked decoder never throws or overreads", () =>
            {
                var rnd = new Random(77);
                string[] chunkSeeds = { "5\r\nhello\r\n0\r\n\r\n", "a;x=1\r\n0123456789\r\n1\r\nz\r\n0\r\nT: v\r\n\r\n", "0\r\n\r\n" };
                for (int i = 0; i < 30000; i++)
                {
                    byte[] input = Mutate(rnd, L(chunkSeeds[rnd.Next(chunkSeeds.Length)]));
                    var d = new ChunkedDecoder();
                    int pos = 0, guard = 0;
                    while (pos <= input.Length && guard++ < 10000)
                    {
                        int avail = Math.Min(input.Length - pos, rnd.Next(1, 8));
                        int consumed, off, cnt;
                        ChunkedStatus st;
                        try { st = d.Decode(input, pos, avail, out consumed, out off, out cnt); }
                        catch (Exception ex) { throw new AssertException("decoder threw " + ex.GetType().Name + " on " + Escape(input)); }
                        A.True(consumed >= 0 && consumed <= avail, "consumed within input");
                        if (st == ChunkedStatus.Data) A.True(off >= pos && off + cnt <= pos + avail && cnt > 0, "data slice inside input");
                        pos += consumed;
                        if (st == ChunkedStatus.Complete || st == ChunkedStatus.Error) break;
                        if (st == ChunkedStatus.NeedMoreData && pos >= input.Length) break;
                    }
                    A.True(guard < 10000, "decoder made progress");
                }
            });

            t.Add("fuzz: multipart parser throws only HttpException", () =>
            {
                var rnd = new Random(99);
                byte[] seed = WebTests.BuildMultipart("Bnd", WebTests.Field("a", "1"), WebTests.FilePart("f", "x.txt", "text/plain", L("file data")), WebTests.Field("b", "two"));
                for (int i = 0; i < 20000; i++)
                {
                    byte[] input = Mutate(rnd, seed);
                    try
                    {
                        MultipartParser.Parse(input, 0, input.Length, "Bnd", Encoding.UTF8, new HttpValueCollection(), new HttpFileCollection(), null);
                    }
                    catch (HttpException) { }
                    catch (Exception ex) { throw new AssertException("multipart threw " + ex.GetType().Name + ": " + ex.Message + " on " + Escape(input)); }
                }
            });

            t.Add("fuzz: url, cookie and html decoders never throw", () =>
            {
                var rnd = new Random(3);
                const string alphabet = "%+&=;a1 \"\\\r\n\u00e9\u4e16#xX;&#";
                for (int i = 0; i < 30000; i++)
                {
                    var sb = new StringBuilder();
                    int len = rnd.Next(0, 40);
                    for (int k = 0; k < len; k++) sb.Append(alphabet[rnd.Next(alphabet.Length)]);
                    string s = sb.ToString();
                    try
                    {
                        UrlEncodedParser.Parse(s, new HttpValueCollection(), Encoding.UTF8, 0);
                        byte[] b = Encoding.UTF8.GetBytes(s);
                        UrlEncodedParser.Parse(b, 0, b.Length, new HttpValueCollection(), Encoding.UTF8, 0);
                        HttpCookieCollection.ParseInto(s, new HttpCookieCollection());
                        WebApp.HtmlDecode(s);
                        WebApp.UrlDecode(s);
                        HeaderValue.GetParameter("x; " + s, "a");
                        A.Eq(s, WebApp.UrlDecode(WebApp.UrlEncode(s)), "url round trip");
                        A.Eq(s, WebApp.HtmlDecode(WebApp.HtmlEncode(s)), "html round trip");
                    }
                    catch (AssertException) { throw; }
                    catch (Exception ex) { throw new AssertException("decoder threw " + ex.GetType().Name + " on <" + s + ">"); }
                }
            });

            t.Add("fuzz: server survives mutated requests without leaking connections", () =>
            {
                using (var s = ServerFixture.StartCustom(new ServerOptions { RequestHeadersTimeout = TimeSpan.FromSeconds(2), BodyReadTimeout = TimeSpan.FromSeconds(2) }))
                {
                    IPEndPoint ep = s.LocalEndpoints[0];
                    int failures = 0;
                    string firstFailure = null;
                    var threads = new List<Thread>();
                    for (int w = 0; w < 8; w++)
                    {
                        int seed = 1000 + w;
                        var th = new Thread(() =>
                        {
                            var rnd = new Random(seed);
                            for (int i = 0; i < 40; i++)
                            {
                                byte[] input = Mutate(rnd, L(Seeds[rnd.Next(Seeds.Length)]));
                                try
                                {
                                    using (var c = new RawClient(ep, 400))
                                    {
                                        c.Send(input);
                                        try { c.Read(); } catch (IOException) { }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (ex is IOException || ex is System.Net.Sockets.SocketException) continue;
                                    Interlocked.Increment(ref failures);
                                    Interlocked.CompareExchange(ref firstFailure, ex.ToString(), null);
                                }
                            }
                        });
                        threads.Add(th);
                        th.Start();
                    }
                    foreach (var th in threads) th.Join();
                    A.Eq(0, failures, firstFailure);
                    using (var c = new RawClient(ep)) A.Eq("hello", c.Get("/hello").Text, "still serving");
                    var sw = Stopwatch.StartNew();
                    while (s.ActiveConnections > 0 && sw.ElapsedMilliseconds < 5000) Thread.Sleep(20);
                    A.Eq(0, s.ActiveConnections, "all connections released");
                }
                WebApp.BeginHosting(null);
            });
        }

        static string Escape(byte[] b)
        {
            var sb = new StringBuilder();
            foreach (byte x in b)
            {
                if (x == 13) sb.Append("\\r");
                else if (x == 10) sb.Append("\\n");
                else if (x < 32 || x > 126) sb.Append("\\x").Append(x.ToString("x2"));
                else sb.Append((char)x);
            }
            return sb.ToString();
        }
    }
}
