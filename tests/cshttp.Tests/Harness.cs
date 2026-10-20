using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace cshttp.Tests
{
    sealed class TestList
    {
        public readonly List<KeyValuePair<string, Action>> Items = new List<KeyValuePair<string, Action>>();
        public void Add(string name, Action test) { Items.Add(new KeyValuePair<string, Action>(name, test)); }
    }

    static class Program
    {
        static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            var tests = new TestList();
            CoreTests.Register(tests);
            WebTests.Register(tests);
            ServerTests.Register(tests);
            FuzzTests.Register(tests);

            Console.WriteLine("cshttp v2 tests on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            int passed = 0, failed = 0;
            var failures = new List<string>();
            var total = Stopwatch.StartNew();
            foreach (var t in tests.Items)
            {
                if (filter != null && t.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var sw = Stopwatch.StartNew();
                try
                {
                    t.Value();
                    passed++;
                    Console.WriteLine("  ok    " + t.Key + "  (" + sw.ElapsedMilliseconds + " ms)");
                }
                catch (Exception ex)
                {
                    failed++;
                    string msg = ex.GetType().Name + ": " + ex.Message;
                    failures.Add(t.Key + " -> " + msg);
                    Console.WriteLine("  FAIL  " + t.Key + "  " + msg);
                    if (!(ex is AssertException)) Console.WriteLine(ex.StackTrace);
                }
            }
            ServerFixture.Shutdown();
            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed, " + total.Elapsed.TotalSeconds.ToString("0.0") + " s");
            foreach (string f in failures) Console.WriteLine("  FAILED: " + f);
            return failed == 0 ? 0 : 1;
        }
    }

    sealed class AssertException : Exception
    {
        public AssertException(string message) : base(message) { }
    }

    static class A
    {
        public static void Eq<T>(T expected, T actual, string what = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertException((what != null ? what + ": " : "") + "expected <" + Show(expected) + "> but was <" + Show(actual) + ">");
        }

        public static void True(bool condition, string what)
        {
            if (!condition) throw new AssertException("expected true: " + what);
        }

        public static void False(bool condition, string what)
        {
            if (condition) throw new AssertException("expected false: " + what);
        }

        public static void Contains(string haystack, string needle, string what = null)
        {
            if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
                throw new AssertException((what != null ? what + ": " : "") + "expected to contain <" + needle + "> in <" + Show(haystack) + ">");
        }

        public static void NotContains(string haystack, string needle, string what = null)
        {
            if (haystack != null && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0)
                throw new AssertException((what != null ? what + ": " : "") + "did not expect <" + needle + "> in <" + Show(haystack) + ">");
        }

        public static TEx Throws<TEx>(Action action, string what = null) where TEx : Exception
        {
            try { action(); }
            catch (TEx ex) { return ex; }
            catch (Exception ex) { throw new AssertException((what ?? "") + " expected " + typeof(TEx).Name + " but got " + ex.GetType().Name + ": " + ex.Message); }
            throw new AssertException((what ?? "") + " expected " + typeof(TEx).Name + " but nothing was thrown");
        }

        public static void Status(HttpParseResult r, int expected, string what)
        {
            if (expected == 0)
            {
                if (r.Status != HttpParseStatus.Complete) throw new AssertException(what + ": expected Complete but was " + r);
            }
            else if (expected == -1)
            {
                if (r.Status != HttpParseStatus.NeedMoreData) throw new AssertException(what + ": expected NeedMoreData but was " + r);
            }
            else if (r.Status != HttpParseStatus.Error || r.ErrorStatusCode != expected)
                throw new AssertException(what + ": expected error " + expected + " but was " + r);
        }

        static string Show(object o)
        {
            if (o == null) return "null";
            string s = o.ToString();
            if (s.Length > 300) s = s.Substring(0, 300) + "...";
            return s.Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }

    static class Bytes
    {
        public static byte[] L(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        public static string S(byte[] b, int offset, int count)
        {
            var c = new char[count];
            for (int i = 0; i < count; i++) c[i] = (char)b[offset + i];
            return new string(c);
        }

        public static string S(byte[] b) { return S(b, 0, b.Length); }

        public static byte[] Concat(params byte[][] parts)
        {
            var ms = new MemoryStream();
            foreach (var p in parts) ms.Write(p, 0, p.Length);
            return ms.ToArray();
        }
    }

    /// <summary>A response as read by the independent test client.</summary>
    sealed class RawResponse
    {
        public int Status;
        public string Reason;
        public readonly List<KeyValuePair<string, string>> Headers = new List<KeyValuePair<string, string>>();
        public byte[] Body = new byte[0];
        public bool WasChunked;

        public string Text { get { return Encoding.UTF8.GetString(Body); } }

        public string Header(string name)
        {
            foreach (var h in Headers) if (string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)) return h.Value;
            return null;
        }

        public int HeaderCount(string name)
        {
            int n = 0;
            foreach (var h in Headers) if (string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }
    }

    /// <summary>
    /// Minimal HTTP/1.1 client over a raw socket, written independently of cshttp so it can
    /// send malformed input and check exact framing.
    /// </summary>
    sealed class RawClient : IDisposable
    {
        readonly TcpClient _tcp;
        readonly Stream _s;
        readonly byte[] _buf = new byte[256 * 1024];
        int _start, _end;

        public RawClient(IPEndPoint ep, int timeoutMs = 5000)
        {
            _tcp = new TcpClient(ep.AddressFamily) { NoDelay = true };
            _tcp.Connect(ep);
            _tcp.ReceiveTimeout = timeoutMs;
            _tcp.SendTimeout = timeoutMs;
            _s = _tcp.GetStream();
        }

        public RawClient(TcpClient tcp, Stream stream)
        {
            _tcp = tcp;
            _s = stream;
        }

        public int ReceiveTimeout { set { _tcp.ReceiveTimeout = value; } }

        public void Send(string s) { Send(Bytes.L(s)); }

        public void Send(byte[] b)
        {
            _s.Write(b, 0, b.Length);
            _s.Flush();
        }

        public RawResponse Get(string path, string extraHeaders = "")
        {
            Send("GET " + path + " HTTP/1.1\r\nHost: test\r\n" + extraHeaders + "\r\n");
            return Read();
        }

        public RawResponse Read(bool headRequest = false)
        {
            var r = new RawResponse();
            string statusLine = ReadLine();
            string[] parts = statusLine.Split(new[] { ' ' }, 3);
            if (parts.Length < 2 || !parts[0].StartsWith("HTTP/1.")) throw new IOException("Bad status line: " + statusLine);
            r.Status = int.Parse(parts[1]);
            r.Reason = parts.Length > 2 ? parts[2] : "";
            while (true)
            {
                string line = ReadLine();
                if (line.Length == 0) break;
                int colon = line.IndexOf(':');
                if (colon <= 0) throw new IOException("Bad header line: " + line);
                r.Headers.Add(new KeyValuePair<string, string>(line.Substring(0, colon), line.Substring(colon + 1).Trim()));
            }
            if (headRequest || r.Status < 200 || r.Status == 204 || r.Status == 304) return r;

            string te = r.Header("Transfer-Encoding");
            string cl = r.Header("Content-Length");
            if (te != null && te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                r.WasChunked = true;
                var ms = new MemoryStream();
                while (true)
                {
                    string sizeLine = ReadLine();
                    int semi = sizeLine.IndexOf(';');
                    if (semi >= 0) sizeLine = sizeLine.Substring(0, semi);
                    int size = Convert.ToInt32(sizeLine.Trim(), 16);
                    if (size == 0)
                    {
                        while (ReadLine().Length > 0) { }
                        break;
                    }
                    byte[] chunk = ReadBytes(size);
                    ms.Write(chunk, 0, chunk.Length);
                    if (ReadLine().Length != 0) throw new IOException("Missing CRLF after chunk");
                }
                r.Body = ms.ToArray();
            }
            else if (cl != null) r.Body = ReadBytes(int.Parse(cl));
            else r.Body = ReadToEnd();
            return r;
        }

        /// <summary>True if the server closed the connection (EOF or reset) within the wait time.</summary>
        public bool WaitForClose(int waitMs)
        {
            if (_end > _start) return false;
            _tcp.ReceiveTimeout = waitMs;
            try
            {
                int n = _s.Read(_buf, 0, _buf.Length);
                if (n == 0) return true;
                _start = 0;
                _end = n;
                return false;
            }
            catch (IOException ex)
            {
                var se = ex.InnerException as SocketException;
                if (se != null && se.SocketErrorCode == SocketError.TimedOut) return false;
                return true;
            }
        }

        public string ReadLine()
        {
            while (true)
            {
                for (int i = _start; i + 1 < _end; i++)
                {
                    if (_buf[i] == 13 && _buf[i + 1] == 10)
                    {
                        string line = Bytes.S(_buf, _start, i - _start);
                        _start = i + 2;
                        return line;
                    }
                }
                Fill();
            }
        }

        public byte[] ReadBytes(int count)
        {
            var result = new byte[count];
            int got = 0;
            while (got < count)
            {
                if (_end == _start) Fill();
                int n = Math.Min(count - got, _end - _start);
                Buffer.BlockCopy(_buf, _start, result, got, n);
                _start += n;
                got += n;
            }
            return result;
        }

        byte[] ReadToEnd()
        {
            var ms = new MemoryStream();
            ms.Write(_buf, _start, _end - _start);
            _start = _end = 0;
            while (true)
            {
                int n = _s.Read(_buf, 0, _buf.Length);
                if (n <= 0) break;
                ms.Write(_buf, 0, n);
            }
            return ms.ToArray();
        }

        void Fill()
        {
            if (_start > 0)
            {
                Buffer.BlockCopy(_buf, _start, _buf, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            if (_end == _buf.Length) throw new IOException("Client buffer full");
            int n = _s.Read(_buf, _end, _buf.Length - _end);
            if (n <= 0) throw new EndOfStreamException("Server closed the connection");
            _end += n;
        }

        public void Dispose()
        {
            try { _s.Dispose(); } catch { }
            try { _tcp.Close(); } catch { }
        }
    }
}
