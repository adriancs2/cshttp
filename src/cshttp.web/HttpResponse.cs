using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace cshttp
{
    /// <summary>Where response bytes go. Supplied by the server (or by a test).</summary>
    public interface IHttpResponseSink
    {
        void Write(byte[] buffer, int offset, int count);
        void Flush();
        bool IsConnected { get; }
    }

    /// <summary>
    /// The response as an application sees it, with System.Web member names.
    ///
    /// Buffered by default: when the handler finishes, everything is sent with a Content-Length.
    /// Flush() (or BufferOutput = false) sends headers early and switches to chunked encoding.
    /// TransmitFile/WriteFile queue the file and stream it at send time without loading it in memory.
    /// </summary>
    public sealed class HttpResponse
    {
        const int SegmentSize = 16 * 1024;
        static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        sealed class Segment
        {
            public byte[] Data;
            public int Length;
            public string File;
            public long FileOffset;
            public long FileLength;
            public long Size { get { return Data != null ? Length : FileLength; } }
        }

        readonly IHttpResponseSink _sink;
        readonly bool _isHead;
        readonly bool _http11;
        readonly bool _requestKeepAlive;

        int _statusCode = 200;
        string _statusDescription;
        string _contentType = "text/html";
        bool _contentTypeExplicit;
        string _charset = "utf-8";
        Encoding _contentEncoding = Utf8NoBom;
        string _cacheControl = "private";
        string _redirectLocation;
        readonly HttpHeaders _headers = new HttpHeaders();
        readonly HttpCookieCollection _cookies = new HttpCookieCollection();

        readonly List<Segment> _segments = new List<Segment>();
        Encoder _encoder;
        readonly char[] _charBuf = new char[2048];

        bool _headersSent, _ended, _completed, _chunked, _noBodyFraming;
        long _fixedLength = -1;
        long _bodySent;
        bool _keepAlive;
        TextWriter _output;
        HttpCachePolicy _cache;
        Stream _outputStream;

        public HttpResponse(IHttpResponseSink sink, HttpRequestHead request)
        {
            if (sink == null) throw new ArgumentNullException("sink");
            _sink = sink;
            _isHead = request != null && request.IsHead;
            _http11 = request == null || request.VersionMinor >= 1;
            _requestKeepAlive = request == null || request.KeepAlive;
            _keepAlive = _requestKeepAlive;
            BufferOutput = true;
        }

        // ---------------- status and headers ----------------

        public int StatusCode
        {
            get { return _statusCode; }
            set
            {
                EnsureHeadersNotSent("Server cannot set status after HTTP headers have been sent.");
                if (value < 100 || value > 999) throw new ArgumentOutOfRangeException("value");
                _statusCode = value;
                _statusDescription = null;
            }
        }

        public string StatusDescription
        {
            get { return _statusDescription ?? HttpStatus.GetReasonPhrase(_statusCode); }
            set
            {
                EnsureHeadersNotSent("Server cannot set status after HTTP headers have been sent.");
                if (!HttpChars.IsSafeFieldValue(value)) throw new ArgumentException("Invalid status description.");
                _statusDescription = value;
            }
        }

        public int SubStatusCode { get; set; }

        public string Status
        {
            get { return _statusCode + " " + StatusDescription; }
        }

        public string ContentType
        {
            get { return _contentType; }
            set
            {
                EnsureHeadersNotSent("Server cannot set content type after HTTP headers have been sent.");
                _contentType = value;
                _contentTypeExplicit = true;
            }
        }

        /// <summary>Appended as "; charset=..." to text, JSON, JavaScript and XML content types.</summary>
        public string Charset
        {
            get { return _charset; }
            set
            {
                EnsureHeadersNotSent("Server cannot set charset after HTTP headers have been sent.");
                _charset = value;
                if (string.IsNullOrEmpty(value)) return;
                try { SetEncoding(Encoding.GetEncoding(value)); }
                catch (ArgumentException) { }
            }
        }

        public Encoding ContentEncoding
        {
            get { return _contentEncoding; }
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                EnsureHeadersNotSent("Server cannot set content encoding after HTTP headers have been sent.");
                SetEncoding(value);
                _charset = value.WebName;
            }
        }

        /// <summary>Cache-Control value. Defaults to "private" like System.Web. Null omits the header.</summary>
        public string CacheControl
        {
            get { return _cacheControl; }
            set
            {
                EnsureHeadersNotSent("Server cannot set cache control after HTTP headers have been sent.");
                _cacheControl = value;
            }
        }

        public string RedirectLocation
        {
            get { return _redirectLocation; }
            set
            {
                EnsureHeadersNotSent("Cannot redirect after HTTP headers have been sent.");
                if (!HttpChars.IsSafeFieldValue(value)) throw new ArgumentException("Invalid redirect location.");
                _redirectLocation = value;
            }
        }

        /// <summary>System.Web-style cache policy; its settings build the Cache-Control header.</summary>
        public HttpCachePolicy Cache { get { return _cache ?? (_cache = new HttpCachePolicy(this)); } }

        /// <summary>Custom response headers (in addition to the ones the response manages itself).</summary>
        public HttpHeaders Headers { get { return _headers; } }

        public HttpCookieCollection Cookies { get { return _cookies; } }

        /// <summary>Value of the Server header; null (default) omits it. Set by the host.</summary>
        public string ServerHeader { get; set; }

        public void AddHeader(string name, string value) { AppendHeader(name, value); }

        public void AppendHeader(string name, string value)
        {
            EnsureHeadersNotSent("Server cannot append header after HTTP headers have been sent.");
            HttpResponseWriter.ValidateHeader(name, value);
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) { _contentType = value; _contentTypeExplicit = true; return; }
            if (string.Equals(name, "Cache-Control", StringComparison.OrdinalIgnoreCase)) { _cacheControl = value; return; }
            if (string.Equals(name, "Location", StringComparison.OrdinalIgnoreCase)) { _redirectLocation = value; return; }
            _headers.Add(name, value);
        }

        public void SetHeader(string name, string value)
        {
            EnsureHeadersNotSent("Server cannot set header after HTTP headers have been sent.");
            HttpResponseWriter.ValidateHeader(name, value);
            _headers.Set(name, value);
        }

        public void AppendCookie(HttpCookie cookie) { _cookies.Add(cookie); }

        public void SetCookie(HttpCookie cookie) { _cookies.Set(cookie); }

        // ---------------- behaviour flags ----------------

        /// <summary>When false, every write is sent immediately (chunked).</summary>
        public bool BufferOutput { get; set; }

        public bool Buffer { get { return BufferOutput; } set { BufferOutput = value; } }

        /// <summary>Body bytes are dropped; headers (and correct framing) are still sent.</summary>
        public bool SuppressContent { get; set; }

        /// <summary>No effect (there is no IIS); kept for source compatibility.</summary>
        public bool TrySkipIisCustomErrors { get; set; }

        public bool IsRequestBeingRedirected { get; private set; }

        public bool HeadersWritten { get { return _headersSent; } }

        public bool IsClientConnected { get { return _sink.IsConnected; } }

        /// <summary>After completion: whether the connection may be reused.</summary>
        public bool KeepAlive { get { return _keepAlive; } }

        public bool IsCompleted { get { return _completed; } }

        // ---------------- writing ----------------

        public TextWriter Output { get { return _output ?? (_output = new ResponseWriter(this)); } }

        public Stream OutputStream { get { return _outputStream ?? (_outputStream = new ResponseStream(this)); } }

        public void Write(string s)
        {
            if (!CanWrite || string.IsNullOrEmpty(s)) return;
            int i = 0;
            while (i < s.Length)
            {
                int n = Math.Min(_charBuf.Length, s.Length - i);
                s.CopyTo(i, _charBuf, 0, n);
                AppendCharBuf(n);
                i += n;
            }
            AfterWrite();
        }

        public void Write(char ch)
        {
            if (!CanWrite) return;
            _charBuf[0] = ch;
            AppendCharBuf(1);
            AfterWrite();
        }

        public void Write(char[] buffer, int index, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (!CanWrite || count <= 0) return;
            int i = 0;
            while (i < count)
            {
                int n = Math.Min(_charBuf.Length, count - i);
                Array.Copy(buffer, index + i, _charBuf, 0, n);
                AppendCharBuf(n);
                i += n;
            }
            AfterWrite();
        }

        public void Write(object obj)
        {
            if (obj != null) Write(obj.ToString());
        }

        public void BinaryWrite(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            WriteBytes(buffer, 0, buffer.Length);
        }

        public void WriteBytes(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException("count");
            if (!CanWrite || count == 0) return;
            FlushEncoderToSegments();
            while (count > 0)
            {
                Segment tail = Tail(1);
                int n = Math.Min(count, tail.Data.Length - tail.Length);
                System.Buffer.BlockCopy(buffer, offset, tail.Data, tail.Length, n);
                tail.Length += n;
                offset += n;
                count -= n;
            }
            AfterWrite();
        }

        /// <summary>
        /// Transmits a file to the browser with automatic MIME type detection, browser caching (ETag, Last-Modified, 304),
        /// and single byte-range support (206/416) when serving static files.
        /// Returns true if the file exists and was handled; returns false if the file does not exist or its type is not allowed.
        /// </summary>
        public bool TransmitFile(string filename)
        {
            if (string.IsNullOrEmpty(filename) || !CanWrite) return false;

            FileInfo fi;
            try
            {
                fi = new FileInfo(filename);
            }
            catch
            {
                return false;
            }

            if (!fi.Exists) return false;
            if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return false;

            // If the caller already set ContentType explicitly (or already buffered body data like "[234]"),
            // stream the raw file directly without overriding static-file headers.
            if (_contentTypeExplicit || _segments.Count > 0 || _headersSent)
            {
                TransmitRawFile(fi, 0, fi.Length);
                return true;
            }

            string contentType = MimeTypes.Get(fi.Extension);
            if (contentType == null) return false;

            HttpContext ctx = HttpContext.Current;
            HttpRequest req = ctx != null ? ctx.Request : null;

            if (req != null)
            {
                string method = req.HttpMethod;
                if (method != "GET" && method != "HEAD")
                {
                    StatusCode = 405;
                    AppendHeader("Allow", "GET, HEAD");
                    ContentType = "text/plain";
                    Write("Method Not Allowed");
                    return true;
                }
            }

            long length = fi.Length;
            DateTime lastModified = new DateTime(fi.LastWriteTimeUtc.Ticks - (fi.LastWriteTimeUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
            string etag = "\"" + fi.LastWriteTimeUtc.Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "-" + length.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "\"";

            _contentType = contentType;
            if (req != null && req.QueryString["v"] != null)
                _cacheControl = "public, max-age=31536000, immutable";
            else
                _cacheControl = "public, max-age=3600";

            AppendHeader("Accept-Ranges", "bytes");
            AppendHeader("ETag", etag);
            AppendHeader("Last-Modified", HttpDate.Format(lastModified));

            if (req != null && StaticFiles.IsNotModified(req, etag, lastModified))
            {
                StatusCode = 304;
                return true;
            }

            if (req != null)
            {
                string range = req.Head.Headers.Get("Range");
                if (range != null && req.HttpMethod == "GET" && StaticFiles.IfRangeMatches(req, etag, lastModified))
                {
                    long start, end;
                    int r = StaticFiles.ParseRange(range, length, out start, out end);
                    if (r == 416)
                    {
                        StatusCode = 416;
                        AppendHeader("Content-Range", "bytes */" + length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        return true;
                    }
                    if (r == 206)
                    {
                        StatusCode = 206;
                        AppendHeader("Content-Range", "bytes " + start + "-" + end + "/" + length);
                        TransmitRawFile(fi, start, end - start + 1);
                        return true;
                    }
                }
            }

            TransmitRawFile(fi, 0, length);
            return true;
        }

        /// <summary>Queues (part of) a file; it is streamed from disk when the response is sent.</summary>
        public void TransmitFile(string filename, long offset, long length)
        {
            if (filename == null) throw new ArgumentNullException("filename");
            var fi = new FileInfo(filename);
            if (!fi.Exists) throw new FileNotFoundException("File not found.", filename);
            if (offset < 0 || offset > fi.Length) throw new ArgumentOutOfRangeException("offset");
            if (length < 0) length = fi.Length - offset;
            if (offset + length > fi.Length) throw new ArgumentOutOfRangeException("length");
            if (!CanWrite) return;
            TransmitRawFile(fi, offset, length);
        }

        void TransmitRawFile(FileInfo fi, long offset, long length)
        {
            FlushEncoderToSegments();
            if (length > 0) _segments.Add(new Segment { File = fi.FullName, FileOffset = offset, FileLength = length });
            AfterWrite();
        }

        public void WriteFile(string filename) { TransmitFile(filename, 0, -1); }

        public void WriteFile(string filename, bool readIntoMemory) { TransmitFile(filename, 0, -1); }

        public void WriteFile(string filename, long offset, long size) { TransmitFile(filename, offset, size); }

        // ---------------- control ----------------

        /// <summary>Sends headers (if not yet sent) and everything buffered so far.</summary>
        public void Flush()
        {
            if (_completed) return;
            FlushInternal(false);
        }

        public void Clear() { ClearContent(); }

        public void ClearContent()
        {
            _segments.Clear();
            if (_encoder != null) _encoder.Reset();
        }

        public void ClearHeaders()
        {
            EnsureHeadersNotSent("Server cannot clear headers after HTTP headers have been sent.");
            _headers.Clear();
            _cookies.Clear();
            _statusCode = 200;
            _statusDescription = null;
            _contentType = "text/html";
            _contentTypeExplicit = false;
            _cacheControl = "private";
            _redirectLocation = null;
            _cache = null;
            IsRequestBeingRedirected = false;
        }

        /// <summary>Sends everything and stops the handler (throws <see cref="HttpResponseEndException"/>).</summary>
        public void End()
        {
            if (!_completed)
            {
                _completed = true;
                FlushInternal(true);
            }
            _ended = true;
            throw new HttpResponseEndException();
        }

        public void Redirect(string url) { RedirectInternal(url, true, 302); }

        public void Redirect(string url, bool endResponse) { RedirectInternal(url, endResponse, 302); }

        public void RedirectPermanent(string url) { RedirectInternal(url, true, 301); }

        public void RedirectPermanent(string url, bool endResponse) { RedirectInternal(url, endResponse, 301); }

        /// <summary>
        /// Called by the host after the handler returns: sends whatever is left and terminates
        /// the body correctly. Returns whether the connection can be kept alive.
        /// </summary>
        public bool Complete()
        {
            if (!_completed)
            {
                _completed = true;
                FlushInternal(true);
            }
            return _keepAlive;
        }

        /// <summary>Discards everything not yet sent and resets status/headers (used by hosts for error pages).</summary>
        public void Reset()
        {
            EnsureHeadersNotSent("Cannot reset the response after HTTP headers have been sent.");
            ClearContent();
            ClearHeaders();
            SuppressContent = false;
            BufferOutput = true;
            _ended = false;
            _completed = false;
            _charset = "utf-8";
            SetEncoding(Utf8NoBom);
        }

        // ---------------- internals ----------------

        bool CanWrite { get { return !_ended && !_completed; } }

        void EnsureHeadersNotSent(string message)
        {
            if (_headersSent) throw new HttpException(message);
        }

        void SetEncoding(Encoding enc)
        {
            FlushEncoderToSegments();
            _contentEncoding = enc;
            _encoder = null;
        }

        void RedirectInternal(string url, bool endResponse, int status)
        {
            if (url == null) throw new ArgumentNullException("url");
            EnsureHeadersNotSent("Cannot redirect after HTTP headers have been sent.");
            if (!HttpChars.IsSafeFieldValue(url)) throw new ArgumentException("Invalid redirect URL.");
            string target = url.StartsWith("~/", StringComparison.Ordinal) ? url.Substring(1) : url;
            ClearContent();
            StatusCode = status;
            _redirectLocation = target;
            IsRequestBeingRedirected = true;
            _contentType = "text/html";
            Write("<html><head><title>Object moved</title></head><body>\r\n<h2>Object moved to <a href=\""
                + HttpUtility.HtmlAttributeEncode(target) + "\">here</a>.</h2>\r\n</body></html>\r\n");
            if (endResponse) End();
        }

        void AfterWrite()
        {
            if (!BufferOutput) FlushInternal(false);
        }

        Segment Tail(int needed)
        {
            Segment last = _segments.Count > 0 ? _segments[_segments.Count - 1] : null;
            if (last != null && last.Data != null && last.Data.Length - last.Length >= needed) return last;
            var seg = new Segment { Data = new byte[Math.Max(SegmentSize, needed)] };
            _segments.Add(seg);
            return seg;
        }

        void AppendCharBuf(int count)
        {
            if (_encoder == null) _encoder = _contentEncoding.GetEncoder();
            Segment tail = Tail(_contentEncoding.GetMaxByteCount(count));
            tail.Length += _encoder.GetBytes(_charBuf, 0, count, tail.Data, tail.Length, false);
        }

        void FlushEncoderToSegments()
        {
            if (_encoder == null) return;
            Segment tail = Tail(_contentEncoding.GetMaxByteCount(1) + 8);
            tail.Length += _encoder.GetBytes(_charBuf, 0, 0, tail.Data, tail.Length, true);
        }

        void FlushInternal(bool final)
        {
            FlushEncoderToSegments();
            if (!_headersSent) SendHeaders(final);
            bool send = !_noBodyFraming && !SuppressContent;
            for (int i = 0; i < _segments.Count; i++)
            {
                Segment s = _segments[i];
                if (!send) continue;
                if (s.Data != null) SendBody(s.Data, 0, s.Length);
                else SendFile(s);
            }
            _segments.Clear();
            if (final && !_noBodyFraming)
            {
                if (_chunked) _sink.Write(HttpResponseWriter.LastChunk, 0, HttpResponseWriter.LastChunk.Length);
                else if (_fixedLength >= 0 && _bodySent != _fixedLength) _keepAlive = false;
            }
            _sink.Flush();
        }

        void SendHeaders(bool final)
        {
            bool statusForbidsBody = HttpStatus.ForbidsBody(_statusCode);
            _noBodyFraming = _isHead || statusForbidsBody;
            long userLength = ParseUserContentLength();
            bool keepAlive = _requestKeepAlive;
            string userConnection = _headers.GetCombined("Connection");
            if (userConnection != null && userConnection.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0) keepAlive = false;

            var h = new HttpHeaders();
            if (!_headers.Contains("Date")) h.Add("Date", HttpDate.Format(DateTime.UtcNow));
            if (!string.IsNullOrEmpty(ServerHeader) && !_headers.Contains("Server")) h.Add("Server", ServerHeader);
            if (!statusForbidsBody && !string.IsNullOrEmpty(_contentType)) h.Add("Content-Type", FullContentType());
            string cacheControl = _cache != null ? _cache.Compose(_cacheControl) : _cacheControl;
            if (cacheControl != null && !_headers.Contains("Cache-Control")) h.Add("Cache-Control", cacheControl);
            if (_redirectLocation != null) h.Add("Location", _redirectLocation);
            foreach (HttpHeader x in _headers)
            {
                if (IsFramingHeader(x.Name)) continue;
                h.Add(x.Name, x.Value);
            }
            for (int i = 0; i < _cookies.Count; i++) h.Add("Set-Cookie", _cookies[i].ToSetCookieHeader());

            long fixedLength = -1;
            bool chunked = false;
            if (statusForbidsBody)
            {
                // no framing headers at all
            }
            else if (final)
            {
                long len = SuppressContent ? 0 : BufferedLength();
                if (_isHead && len == 0 && userLength >= 0) len = userLength;
                fixedLength = len;
                h.Add("Content-Length", len.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (userLength >= 0)
            {
                fixedLength = userLength;
                h.Add("Content-Length", userLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (_http11)
            {
                chunked = true;
                h.Add("Transfer-Encoding", "chunked");
            }
            else
            {
                keepAlive = false; // HTTP/1.0 without a length: the body ends when the connection closes
            }
            if (!keepAlive) h.Add("Connection", "close");
            else if (!_http11) h.Add("Connection", "keep-alive");

            byte[] head = HttpResponseWriter.BuildHead(_statusCode, _statusDescription, h); // throws on injection, before anything is sent
            _keepAlive = keepAlive;
            _fixedLength = fixedLength;
            _chunked = chunked;
            _headersSent = true;
            _sink.Write(head, 0, head.Length);
        }

        static bool IsFramingHeader(string name)
        {
            return string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Keep-Alive", StringComparison.OrdinalIgnoreCase);
        }

        long ParseUserContentLength()
        {
            string v = _headers.Get("Content-Length");
            long n;
            if (v != null && long.TryParse(v.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n)) return n;
            return -1;
        }

        string FullContentType()
        {
            string ct = _contentType;
            if (string.IsNullOrEmpty(_charset) || ct.IndexOf("charset=", StringComparison.OrdinalIgnoreCase) >= 0) return ct;
            string lower = ct.ToLowerInvariant();
            if (lower.StartsWith("text/", StringComparison.Ordinal) || lower.Contains("json") || lower.Contains("javascript") || lower.Contains("xml"))
                return ct + "; charset=" + _charset;
            return ct;
        }

        long BufferedLength()
        {
            long total = 0;
            for (int i = 0; i < _segments.Count; i++) total += _segments[i].Size;
            return total;
        }

        void SendBody(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            if (_fixedLength >= 0)
            {
                long room = _fixedLength - _bodySent;
                if (room <= 0) { _keepAlive = false; return; }
                if (count > room) { count = (int)room; _keepAlive = false; }
            }
            if (_chunked)
            {
                byte[] header = new byte[20];
                int n = HttpResponseWriter.WriteChunkHeader(header, 0, count);
                _sink.Write(header, 0, n);
                _sink.Write(data, offset, count);
                _sink.Write(HttpResponseWriter.Crlf, 0, 2);
            }
            else _sink.Write(data, offset, count);
            _bodySent += count;
        }

        void SendFile(Segment s)
        {
            byte[] buffer = new byte[64 * 1024];
            using (var fs = new FileStream(s.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan))
            {
                fs.Seek(s.FileOffset, SeekOrigin.Begin);
                long remaining = s.FileLength;
                while (remaining > 0)
                {
                    int n = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (n <= 0) { _keepAlive = false; break; } // file shrank; framing is now wrong
                    SendBody(buffer, 0, n);
                    remaining -= n;
                }
            }
        }

        sealed class ResponseWriter : TextWriter
        {
            readonly HttpResponse _r;
            public ResponseWriter(HttpResponse r) { _r = r; }
            public override Encoding Encoding { get { return _r.ContentEncoding; } }
            public override void Write(char value) { _r.Write(value); }
            public override void Write(string value) { _r.Write(value); }
            public override void Write(char[] buffer, int index, int count) { _r.Write(buffer, index, count); }
            public override void Flush() { _r.Flush(); }
        }

        sealed class ResponseStream : Stream
        {
            readonly HttpResponse _r;
            public ResponseStream(HttpResponse r) { _r = r; }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { _r.Flush(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { _r.WriteBytes(buffer, offset, count); }
        }
    }
}
