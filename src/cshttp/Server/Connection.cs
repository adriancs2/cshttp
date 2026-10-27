using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace cshttp
{
    /// <summary>
    /// One client connection in cshttp.server2:
    ///   - Zero OS threads held while waiting for the first byte of a request (idle keep-alive or newly accepted socket).
    ///   - Once the first packet arrives on the IOCP thread, dispatches to a ThreadPool Worker thread (never blocking IOCP threads)
    ///     and runs the entire request + keep-alive active burst synchronously.
    /// </summary>
    internal sealed class Connection : IHttpResponseSink
    {
        static readonly AsyncCallback OnFirstReadCallback = OnFirstReadComplete;
        static readonly WaitCallback OnInitCallback = state => ((Connection)state).InitializeConnection();
        static readonly WaitCallback OnRunActiveWorkerCallback = state => ((Connection)state).RunActiveWorker();

        readonly HttpServer _server;
        readonly ServerOptions _o;
        readonly Socket _socket;
        readonly ServerBinding _binding;
        Stream _stream;
        BufferedStream _out;
        readonly byte[] _buf;
        int _start, _end;
        int _served;
        volatile bool _idle;
        long _idleDeadlineTicks;
        int _closed;
        IPEndPoint _remote, _local;

        public Connection(HttpServer server, Socket socket, ServerBinding binding)
        {
            _server = server;
            _o = server.Options;
            _socket = socket;
            _binding = binding;
            _buf = new byte[Math.Max(16 * 1024, _o.Parser.MaxHeaderBytes + 4096)];
        }

        /// <summary>Starts the connection without allocating a dedicated OS thread.</summary>
        public void Start()
        {
            if (_binding.IsHttps)
            {
                // TLS handshake uses synchronous AuthenticateAsServer with TlsHandshakeTimeout;
                // run on a ThreadPool worker so the accept loop is never blocked by a slow TLS client.
                ThreadPool.QueueUserWorkItem(OnInitCallback, this);
            }
            else
            {
                InitializeConnection();
            }
        }

        void InitializeConnection()
        {
            try
            {
                _remote = (IPEndPoint)_socket.RemoteEndPoint;
                _local = (IPEndPoint)_socket.LocalEndPoint;
                _socket.NoDelay = true;
                _socket.SendTimeout = Ms(_o.SendTimeout);
                Stream ns = new NetworkStream(_socket, true);
                if (_binding.IsHttps)
                {
                    _socket.ReceiveTimeout = Ms(_o.TlsHandshakeTimeout);
                    var ssl = new SslStream(ns, false);
                    ssl.AuthenticateAsServer(_binding.Certificate, false, _o.SslProtocols, false);
                    _stream = ssl;
                }
                else
                {
                    _stream = ns;
                }
                _out = new BufferedStream(_stream, 16 * 1024);

                // Register async IOCP wait for the first byte so the accept thread returns immediately.
                ArmIdleRead();
            }
            catch (Exception ex)
            {
                if (!IsDisconnect(ex)) _server.LogMessage("Connection error: " + ex);
                FinishAndRemove();
            }
        }

        /// <summary>
        /// Registers an asynchronous zero-thread IOCP read for the first byte of the next request.
        /// If the read completes synchronously (bytes already in kernel buffer), dispatches to a ThreadPool Worker thread.
        /// </summary>
        void ArmIdleRead()
        {
            if (_server.IsStopping)
            {
                FinishAndRemove();
                return;
            }

            _start = _end = 0;
            TimeSpan waitTimeout = _served == 0 ? _o.RequestHeadersTimeout : _o.KeepAliveTimeout;
            Interlocked.Exchange(ref _idleDeadlineTicks, Now() + ToTicks(waitTimeout));
            _idle = true;

            if (_server.IsStopping)
            {
                _idle = false;
                FinishAndRemove();
                return;
            }

            IAsyncResult ar;
            try
            {
                ar = _stream.BeginRead(_buf, 0, _buf.Length, OnFirstReadCallback, this);
            }
            catch (Exception ex)
            {
                _idle = false;
                if (!IsDisconnect(ex)) _server.LogMessage("Connection error: " + ex);
                FinishAndRemove();
                return;
            }

            if (!ar.CompletedSynchronously) return;

            _idle = false;
            Interlocked.Exchange(ref _idleDeadlineTicks, 0);
            int n;
            try
            {
                n = _stream.EndRead(ar);
            }
            catch (Exception ex)
            {
                if (!IsDisconnect(ex)) _server.LogMessage("Connection error: " + ex);
                FinishAndRemove();
                return;
            }

            if (n <= 0 || _server.IsStopping)
            {
                FinishAndRemove();
                return;
            }

            _end = n;
            ThreadPool.QueueUserWorkItem(OnRunActiveWorkerCallback, this);
        }

        static void OnFirstReadComplete(IAsyncResult ar)
        {
            if (ar.CompletedSynchronously) return;
            var conn = (Connection)ar.AsyncState;
            conn.CompleteAsyncFirstRead(ar);
        }

        void CompleteAsyncFirstRead(IAsyncResult ar)
        {
            _idle = false;
            Interlocked.Exchange(ref _idleDeadlineTicks, 0);

            int n;
            try
            {
                n = _stream.EndRead(ar);
            }
            catch (Exception ex)
            {
                if (!IsDisconnect(ex)) _server.LogMessage("Connection error: " + ex);
                FinishAndRemove();
                return;
            }

            if (n <= 0 || _server.IsStopping)
            {
                FinishAndRemove();
                return;
            }

            _end = n;
            // Hand off from the OS IOCP completion thread to a ThreadPool Worker thread so blocking DB/file handlers
            // never starve IOCP completion callbacks across other sockets.
            ThreadPool.QueueUserWorkItem(OnRunActiveWorkerCallback, this);
        }

        /// <summary>
        /// Runs on a ThreadPool Worker thread. Processes the current request, any pipelined requests,
        /// and any immediate keep-alive follow-up requests already sitting in the socket buffer before
        /// returning the connection to zero-thread IOCP idle state.
        /// </summary>
        void RunActiveWorker()
        {
            _server.EnterActiveRequest();
            try
            {
                while (!_server.IsStopping)
                {
                    long headDeadline = Now() + ToTicks(_o.RequestHeadersTimeout);
                    if (!ProcessRequest(headDeadline))
                    {
                        FinishAndRemove();
                        return;
                    }
                    _served++;
                    if (_o.MaxRequestsPerConnection > 0 && _served >= _o.MaxRequestsPerConnection)
                    {
                        FinishAndRemove();
                        return;
                    }

                    // If pipelined bytes are already in _buf, loop immediately on the same worker thread.
                    if (_start < _end) continue;

                    // Fast check: if the client/Nginx already sent the next keep-alive request into the OS socket buffer,
                    // read it synchronously right here without an IOCP round-trip!
                    if (_socket.Available > 0)
                    {
                        _start = _end = 0;
                        int n = Read(Ms(_o.RequestHeadersTimeout));
                        if (n > 0)
                        {
                            _end = n;
                            continue;
                        }
                        FinishAndRemove();
                        return;
                    }

                    // Otherwise, connection is genuinely idle: release the worker thread and arm zero-thread IOCP wait.
                    break;
                }
            }
            catch (Exception ex)
            {
                if (!IsDisconnect(ex)) _server.LogMessage("Connection error: " + ex);
                FinishAndRemove();
                return;
            }
            finally
            {
                _server.LeaveActiveRequest();
            }

            if (!_server.IsStopping)
            {
                ArmIdleRead();
            }
            else
            {
                FinishAndRemove();
            }
        }

        /// <summary>Handles one request whose first byte(s) have already arrived in _buf.</summary>
        bool ProcessRequest(long headDeadline)
        {
            // ---- 1. Receive the rest of the head ----
            HttpRequestHead head;
            while (true)
            {
                if (_end > _start)
                {
                    HttpParseResult r = HttpRequestParser.ParseHead(_buf, _start, _end - _start, _o.Parser, out head);
                    if (r.Status == HttpParseStatus.Complete) { _start += r.Consumed; break; }
                    if (r.Status == HttpParseStatus.Error) { SendError(r.ErrorStatusCode, r.ErrorMessage); return false; }
                    if (_end == _buf.Length)
                    {
                        if (_start > 0) Compact();
                        else { SendError(431, "Request headers too large."); return false; }
                    }
                }

                long left = headDeadline - Now();
                if (left <= 0) { SendError(408, "Timed out receiving request headers."); return false; }
                int timeout = TicksToMs(left);

                int n = Read(timeout);
                if (n == 0) return false;
                if (n < 0)
                {
                    if (_end > _start) SendError(408, "Timed out receiving request headers.");
                    return false;
                }
                _end += n;
            }

            // ---- 2. Receive the body ----
            byte[] body = null;
            int bodyLength = 0;
            if (head.BodyKind == HttpBodyKind.ContentLength)
            {
                long declared = head.ContentLength;
                if (declared > _o.MaxRequestBodySize || declared > int.MaxValue - 64) { SendError(413, "Request body too large."); return false; }
                int len = (int)declared;
                body = new byte[len];
                int have = Math.Min(len, _end - _start);
                Buffer.BlockCopy(_buf, _start, body, 0, have);
                _start += have;
                if (have < len)
                {
                    if (head.ExpectContinue) SendRaw(HttpResponseWriter.Continue100);
                    if (!ReadExact(body, have, len - have)) return false;
                }
                bodyLength = len;
            }
            else if (head.BodyKind == HttpBodyKind.Chunked)
            {
                if (head.ExpectContinue) SendRaw(HttpResponseWriter.Continue100);
                if (!ReadChunked(out body, out bodyLength)) return false;
            }

            // ---- 3. Dispatch ----
            _server.CountRequest();
            var info = new RequestConnectionInfo
            {
                RemoteAddress = RequestConnectionInfo.Normalize(_remote.Address),
                PeerAddress = RequestConnectionInfo.Normalize(_remote.Address),
                RemotePort = _remote.Port,
                LocalAddress = RequestConnectionInfo.Normalize(_local.Address),
                LocalPort = _local.Port,
                IsSecure = _binding.IsHttps
            };
            ApplyForwardedHeaders(head, info);

            var request = new HttpRequest(head, body, 0, bodyLength, info, _o.Request);
            var response = new HttpResponse(this, head) { ServerHeader = _o.ServerHeader };
            var context = new HttpContext(request, response);

            HttpContext.Current = context;
            try
            {
                _server.Handler(context);
            }
            catch (HttpResponseEndException) { }
            catch (Exception ex)
            {
                if (IsDisconnect(ex) && !IsConnected) return false;
                if (!HandleError(context, ex)) return false;
            }
            finally
            {
                HttpContext.Current = null;
            }

            bool keep;
            try
            {
                keep = response.Complete();
            }
            catch (Exception ex)
            {
                if (IsDisconnect(ex)) return false;
                if (!HandleError(context, ex)) return false; // e.g. an invalid header value set by the handler
                try { keep = response.Complete(); }
                catch { return false; }
            }
            return keep && head.KeepAlive && !_server.IsStopping;
        }

        bool HandleError(HttpContext context, Exception ex)
        {
            var hx = ex as HttpException;
            int status = hx != null && hx.GetHttpCode() >= 400 && hx.GetHttpCode() < 600 ? hx.GetHttpCode() : 500;
            if (status >= 500) _server.ReportError(context, ex);
            HttpResponse r = context.Response;
            if (r.HeadersWritten) return false; // too late for an error page; closing signals the failure
            try
            {
                r.Reset();
                r.StatusCode = status;
                r.ContentType = "text/html";
                r.CacheControl = "no-store";
                string title = status + " " + HttpStatus.GetReasonPhrase(status);
                string detail = _o.DetailedErrors
                    ? "<pre>" + WebApp.HtmlEncode(ex.ToString()) + "</pre>"
                    : (hx != null && status < 500 ? "<p>" + WebApp.HtmlEncode(hx.Message) + "</p>" : string.Empty);
                r.Write("<!DOCTYPE html><html><head><title>" + title + "</title></head><body><h1>" + title + "</h1>" + detail + "</body></html>");
                return true;
            }
            catch
            {
                return false;
            }
        }

        bool ReadExact(byte[] dest, int offset, int count)
        {
            long started = Now();
            long received = 0;
            while (count > 0)
            {
                int n;
                try
                {
                    _socket.ReceiveTimeout = Ms(_o.BodyReadTimeout);
                    n = _stream.Read(dest, offset, count);
                }
                catch (IOException ex)
                {
                    if (IsTimeout(ex)) SendError(408, "Timed out receiving the request body.");
                    return false;
                }
                if (n == 0) return false;
                offset += n;
                count -= n;
                received += n;
                if (!RateOk(started, received)) { SendError(408, "Request body is arriving too slowly."); return false; }
            }
            return true;
        }

        bool ReadChunked(out byte[] body, out int length)
        {
            body = null;
            length = 0;
            var decoder = new ChunkedDecoder { AllowBareLf = _o.Parser.AllowBareLf };
            var ms = new MemoryStream();
            long started = Now();
            long received = 0;
            while (true)
            {
                if (_end > _start)
                {
                    int consumed, dataOffset, dataCount;
                    ChunkedStatus st = decoder.Decode(_buf, _start, _end - _start, out consumed, out dataOffset, out dataCount);
                    _start += consumed;
                    if (st == ChunkedStatus.Data)
                    {
                        if (ms.Length + dataCount > _o.MaxRequestBodySize || ms.Length + dataCount > int.MaxValue - 64)
                        {
                            SendError(413, "Request body too large.");
                            return false;
                        }
                        ms.Write(_buf, dataOffset, dataCount);
                        continue;
                    }
                    if (st == ChunkedStatus.Complete) break;
                    if (st == ChunkedStatus.Error) { SendError(400, "Invalid chunked body: " + decoder.ErrorMessage); return false; }
                }
                if (_start == _end) _start = _end = 0;
                else if (_end == _buf.Length) Compact();
                int n = Read(Ms(_o.BodyReadTimeout));
                if (n == 0) return false;
                if (n < 0) { SendError(408, "Timed out receiving the request body."); return false; }
                _end += n;
                received += n;
                if (!RateOk(started, received)) { SendError(408, "Request body is arriving too slowly."); return false; }
            }
            body = ms.GetBuffer();
            length = (int)ms.Length;
            return true;
        }

        bool RateOk(long started, long received)
        {
            if (_o.MinRequestBodyBytesPerSecond <= 0) return true;
            double seconds = (Now() - started) / (double)Stopwatch.Frequency;
            if (seconds <= _o.MinDataRateGracePeriod.TotalSeconds) return true;
            return received / seconds >= _o.MinRequestBodyBytesPerSecond;
        }

        /// <summary>Synchronous read into _buf at _end while already inside an active request.</summary>
        int Read(int timeoutMs)
        {
            try
            {
                _socket.ReceiveTimeout = Math.Max(1, timeoutMs);
                return _stream.Read(_buf, _end, _buf.Length - _end);
            }
            catch (IOException ex)
            {
                if (IsTimeout(ex)) return -1;
                throw;
            }
        }

        void Compact()
        {
            if (_start == 0) return;
            Buffer.BlockCopy(_buf, _start, _buf, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }

        void ApplyForwardedHeaders(HttpRequestHead head, RequestConnectionInfo info)
        {
            if (_o.TrustedProxies.Count == 0 || !IsTrusted(info.PeerAddress)) return;
            string xff = head.Headers.GetCombined("X-Forwarded-For");
            if (xff != null)
            {
                string[] parts = xff.Split(',');
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    IPAddress ip;
                    if (!IPAddress.TryParse(parts[i].Trim(), out ip)) break;
                    ip = RequestConnectionInfo.Normalize(ip);
                    info.RemoteAddress = ip;
                    if (!IsTrusted(ip)) break;
                }
            }
            string proto = head.Headers.GetCombined("X-Forwarded-Proto");
            if (proto != null)
            {
                string[] parts = proto.Split(',');
                string last = parts[parts.Length - 1].Trim();
                if (string.Equals(last, "https", StringComparison.OrdinalIgnoreCase)) info.IsSecure = true;
                else if (string.Equals(last, "http", StringComparison.OrdinalIgnoreCase)) info.IsSecure = false;
            }
        }

        bool IsTrusted(IPAddress ip)
        {
            foreach (IPAddress p in _o.TrustedProxies)
                if (RequestConnectionInfo.Normalize(p).Equals(ip)) return true;
            return false;
        }

        void SendError(int status, string message)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(status + " " + HttpStatus.GetReasonPhrase(status) + (message != null ? ": " + message : string.Empty) + "\r\n");
                var h = new HttpHeaders();
                h.Add("Date", HttpDate.Format(DateTime.UtcNow));
                if (!string.IsNullOrEmpty(_o.ServerHeader)) h.Add("Server", _o.ServerHeader);
                h.Add("Content-Type", "text/plain; charset=utf-8");
                h.Add("Content-Length", body.Length.ToString());
                h.Add("Connection", "close");
                byte[] head = HttpResponseWriter.BuildHead(status, null, h);
                _out.Write(head, 0, head.Length);
                _out.Write(body, 0, body.Length);
                _out.Flush();
            }
            catch { }
        }

        void SendRaw(byte[] data)
        {
            _out.Write(data, 0, data.Length);
            _out.Flush();
        }

        // ---- IHttpResponseSink ----

        public void Write(byte[] buffer, int offset, int count) { _out.Write(buffer, offset, count); }

        public void Flush() { _out.Flush(); }

        public bool IsConnected
        {
            get
            {
                if (Volatile.Read(ref _closed) != 0) return false;
                try { return !(_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0); }
                catch { return false; }
            }
        }

        // ---- lifetime ----

        public void CheckIdleTimeout(long nowTicks)
        {
            if (!_idle) return;
            long deadline = Interlocked.Read(ref _idleDeadlineTicks);
            if (deadline > 0 && nowTicks >= deadline)
            {
                Abort();
            }
        }

        public void CloseIfIdle()
        {
            if (_idle) Abort();
        }

        public void Abort()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _socket.Close(0); } catch { }
            _server.Remove(this);
        }

        void FinishAndRemove()
        {
            Close();
            _server.Remove(this);
        }

        void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { if (_out != null) _out.Flush(); } catch { }
            try { _socket.Shutdown(SocketShutdown.Send); } catch { }
            try
            {
                // Drain briefly so unread client bytes do not turn our close into a reset that destroys the response.
                _socket.ReceiveTimeout = 200;
                byte[] tmp = new byte[4096];
                int total = 0;
                while (total < 64 * 1024)
                {
                    int n = _socket.Receive(tmp);
                    if (n <= 0) break;
                    total += n;
                }
            }
            catch { }
            try { if (_stream != null) _stream.Dispose(); } catch { }
            try { _socket.Close(); } catch { }
        }

        static bool IsTimeout(Exception ex)
        {
            var se = ex as SocketException ?? ex.InnerException as SocketException;
            return se != null && se.SocketErrorCode == SocketError.TimedOut;
        }

        static bool IsDisconnect(Exception ex)
        {
            return ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is AuthenticationException;
        }

        static long Now() { return Stopwatch.GetTimestamp(); }

        static long ToTicks(TimeSpan t) { return (long)(t.TotalSeconds * Stopwatch.Frequency); }

        static int TicksToMs(long ticks) { return (int)Math.Max(1, Math.Min(int.MaxValue, ticks * 1000 / Stopwatch.Frequency)); }

        static int Ms(TimeSpan t) { return (int)Math.Max(1, Math.Min(int.MaxValue, t.TotalMilliseconds)); }
    }
}
