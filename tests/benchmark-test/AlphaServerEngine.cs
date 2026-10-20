using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace cshttp.Benchmark.Alpha10Internal
{
    /// <summary>
    /// Exact architecture of cshttp.server.alpha.1.0:
    ///   - One dedicated OS Thread per connection (new Thread(conn.Run).Start())
    ///   - 100% synchronous blocking Socket/Stream I/O
    ///   - Holds the OS thread while waiting between keep-alive requests
    /// </summary>
    public sealed class AlphaHttpServer : IDisposable
    {
        readonly ServerOptions _o;
        readonly List<Socket> _listeners = new List<Socket>();
        readonly List<Thread> _acceptThreads = new List<Thread>();
        readonly List<IPEndPoint> _endpoints = new List<IPEndPoint>();
        readonly ConcurrentDictionary<AlphaConnection, bool> _connections = new ConcurrentDictionary<AlphaConnection, bool>();
        readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(false);
        Action<HttpContext> _handler;
        volatile bool _stopping;
        int _started, _stopCalled;
        long _totalConnections, _totalRequests;

        public AlphaHttpServer(ServerOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            _o = options;
        }

        public ServerOptions Options { get { return _o; } }
        public IList<IPEndPoint> LocalEndpoints { get { return _endpoints.AsReadOnly(); } }
        public int ActiveConnections { get { return _connections.Count; } }
        public bool IsStopping { get { return _stopping; } }
        internal Action<HttpContext> Handler { get { return _handler; } }

        public void Start(Action<HttpContext> handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            if (Interlocked.Exchange(ref _started, 1) == 1) throw new InvalidOperationException("The server was already started.");
            _handler = handler;
            HostingEnvironment.BeginHosting(_o.ContentRoot);
            foreach (ServerBinding binding in _o.Bindings)
            {
                var socket = new Socket(binding.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                if (binding.Address.Equals(IPAddress.IPv6Any)) socket.DualMode = true;
                socket.Bind(new IPEndPoint(binding.Address, binding.Port));
                socket.Listen(512);
                _listeners.Add(socket);
                _endpoints.Add((IPEndPoint)socket.LocalEndPoint);
                ServerBinding b = binding;
                _acceptThreads.Add(new Thread(() => AcceptLoop(socket, b)) { IsBackground = true, Name = "cshttp.alpha.1.0 accept " + b });
            }
            foreach (Thread t in _acceptThreads) t.Start();
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopCalled, 1) == 1) { _stopped.Wait(); return; }
            _stopping = true;
            foreach (Socket s in _listeners) SafeClose(s);
            DateTime deadline = DateTime.UtcNow + _o.ShutdownTimeout;
            while (true)
            {
                foreach (AlphaConnection c in _connections.Keys) c.CloseIfIdle();
                if (_connections.IsEmpty) break;
                if (DateTime.UtcNow >= deadline)
                {
                    foreach (AlphaConnection c in _connections.Keys) c.Abort();
                    break;
                }
                Thread.Sleep(10);
            }
            foreach (Thread t in _acceptThreads) t.Join(1000);
            _stopped.Set();
        }

        public void Dispose() { Stop(); }

        void AcceptLoop(Socket listener, ServerBinding binding)
        {
            while (!_stopping)
            {
                Socket client;
                try { client = listener.Accept(); }
                catch { break; }
                if (_stopping) { SafeClose(client); break; }
                if (_connections.Count >= _o.MaxConnections)
                {
                    SafeClose(client);
                    continue;
                }
                var conn = new AlphaConnection(this, client, binding);
                _connections[conn] = true;
                Interlocked.Increment(ref _totalConnections);
                try
                {
                    new Thread(conn.Run) { IsBackground = true, Name = "cshttp.alpha.1.0 connection" }.Start();
                }
                catch
                {
                    bool ignored;
                    _connections.TryRemove(conn, out ignored);
                    SafeClose(client);
                }
            }
        }

        internal void Remove(AlphaConnection c)
        {
            bool ignored;
            _connections.TryRemove(c, out ignored);
        }

        internal void CountRequest() { Interlocked.Increment(ref _totalRequests); }

        static void SafeClose(Socket s) { try { s.Close(0); } catch { } }
    }

    internal sealed class AlphaConnection : IHttpResponseSink
    {
        readonly AlphaHttpServer _server;
        readonly ServerOptions _o;
        readonly Socket _socket;
        readonly ServerBinding _binding;
        Stream _stream;
        BufferedStream _out;
        readonly byte[] _buf;
        int _start, _end;
        volatile bool _idle;
        volatile bool _closed;
        IPEndPoint _remote, _local;

        public AlphaConnection(AlphaHttpServer server, Socket socket, ServerBinding binding)
        {
            _server = server;
            _o = server.Options;
            _socket = socket;
            _binding = binding;
            _buf = new byte[Math.Max(16 * 1024, _o.Parser.MaxHeaderBytes + 4096)];
        }

        public void Run()
        {
            try
            {
                _remote = (IPEndPoint)_socket.RemoteEndPoint;
                _local = (IPEndPoint)_socket.LocalEndPoint;
                _socket.NoDelay = true;
                _socket.SendTimeout = Ms(_o.SendTimeout);
                _stream = new NetworkStream(_socket, true);
                _out = new BufferedStream(_stream, 16 * 1024);

                int served = 0;
                while (!_server.IsStopping)
                {
                    if (!ProcessRequest(served == 0)) break;
                    served++;
                    if (_o.MaxRequestsPerConnection > 0 && served >= _o.MaxRequestsPerConnection) break;
                }
            }
            catch { }
            finally
            {
                Close();
                _server.Remove(this);
            }
        }

        bool ProcessRequest(bool first)
        {
            HttpRequestHead head;
            long headDeadline = 0;
            while (true)
            {
                if (_start == _end) _start = _end = 0;
                if (_end > _start)
                {
                    if (headDeadline == 0) headDeadline = Now() + ToTicks(_o.RequestHeadersTimeout);
                    HttpParseResult r = HttpRequestParser.ParseHead(_buf, _start, _end - _start, _o.Parser, out head);
                    if (r.Status == HttpParseStatus.Complete) { _start += r.Consumed; break; }
                    if (r.Status == HttpParseStatus.Error) return false;
                    if (_end == _buf.Length)
                    {
                        if (_start > 0) Compact();
                        else return false;
                    }
                }

                int timeout;
                if (_end == _start)
                {
                    _idle = true;
                    if (_server.IsStopping) return false;
                    timeout = Ms(first ? _o.RequestHeadersTimeout : _o.KeepAliveTimeout);
                }
                else
                {
                    long left = headDeadline - Now();
                    if (left <= 0) return false;
                    timeout = TicksToMs(left);
                }

                int n;
                try { n = Read(timeout); }
                finally { _idle = false; }
                if (n <= 0) return false;
                _end += n;
            }

            byte[] body = null;
            int bodyLength = 0;
            if (head.BodyKind == HttpBodyKind.ContentLength && head.ContentLength > 0)
            {
                int len = (int)head.ContentLength;
                body = new byte[len];
                int have = Math.Min(len, _end - _start);
                Buffer.BlockCopy(_buf, _start, body, 0, have);
                _start += have;
                while (have < len)
                {
                    _socket.ReceiveTimeout = Ms(_o.BodyReadTimeout);
                    int n = _stream.Read(body, have, len - have);
                    if (n <= 0) return false;
                    have += n;
                }
                bodyLength = len;
            }

            _server.CountRequest();
            var info = new RequestConnectionInfo
            {
                RemoteAddress = RequestConnectionInfo.Normalize(_remote.Address),
                PeerAddress = RequestConnectionInfo.Normalize(_remote.Address),
                RemotePort = _remote.Port,
                LocalAddress = RequestConnectionInfo.Normalize(_local.Address),
                LocalPort = _local.Port,
                IsSecure = false
            };

            var request = new HttpRequest(head, body, 0, bodyLength, info, _o.Request);
            var response = new HttpResponse(this, head) { ServerHeader = _o.ServerHeader };
            var context = new HttpContext(request, response);

            HttpContext.Current = context;
            try { _server.Handler(context); }
            catch (HttpResponseEndException) { }
            finally { HttpContext.Current = null; }

            bool keep = response.Complete();
            return keep && head.KeepAlive && !_server.IsStopping;
        }

        int Read(int timeoutMs)
        {
            try
            {
                _socket.ReceiveTimeout = Math.Max(1, timeoutMs);
                return _stream.Read(_buf, _end, _buf.Length - _end);
            }
            catch { return -1; }
        }

        void Compact()
        {
            if (_start == 0) return;
            Buffer.BlockCopy(_buf, _start, _buf, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }

        public void Write(byte[] buffer, int offset, int count) { _out.Write(buffer, offset, count); }
        public void Flush() { _out.Flush(); }
        public bool IsConnected { get { return !_closed; } }
        public void CloseIfIdle() { if (_idle) Abort(); }
        public void Abort() { _closed = true; try { _socket.Close(0); } catch { } }

        void Close()
        {
            if (_closed) return;
            _closed = true;
            try { if (_out != null) _out.Flush(); } catch { }
            try { if (_stream != null) _stream.Dispose(); } catch { }
            try { _socket.Close(); } catch { }
        }

        static long Now() { return Stopwatch.GetTimestamp(); }
        static long ToTicks(TimeSpan t) { return (long)(t.TotalSeconds * Stopwatch.Frequency); }
        static int TicksToMs(long ticks) { return (int)Math.Max(1, Math.Min(int.MaxValue, ticks * 1000 / Stopwatch.Frequency)); }
        static int Ms(TimeSpan t) { return (int)Math.Max(1, Math.Min(int.MaxValue, t.TotalMilliseconds)); }
    }
}
