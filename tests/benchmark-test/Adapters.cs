using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace cshttp.Benchmark
{
    // =========================================================================
    // COMMON BENCHMARK INTERFACE
    // =========================================================================
    interface IBenchServer : IDisposable
    {
        string Name { get; }
        IPEndPoint Endpoint { get; }
        int ActiveConnections { get; }
        void Start(Action<HttpContext> handler);
        void Stop();
    }

    // =========================================================================
    // STRATEGY 1: cshttp.server.alpha.1.0 (1 OS Thread per Connection + Sync Blocking I/O)
    // =========================================================================
    sealed class AlphaServerAdapter : IBenchServer
    {
        readonly Alpha10Internal.AlphaHttpServer _server;
        public string Name { get { return "1. cshttp.server.alpha.1.0 (1 OS Thread/Conn, Sync I/O)"; } }
        public IPEndPoint Endpoint { get { return _server.LocalEndpoints[0]; } }
        public int ActiveConnections { get { return _server.ActiveConnections; } }

        public AlphaServerAdapter(int maxConnections)
        {
            var opts = new ServerOptions
            {
                MaxConnections = maxConnections,
                KeepAliveTimeout = TimeSpan.FromSeconds(60),
                RequestHeadersTimeout = TimeSpan.FromSeconds(30)
            };
            opts.ListenHttp(IPAddress.Loopback, 0);
            _server = new Alpha10Internal.AlphaHttpServer(opts);
        }

        public void Start(Action<HttpContext> handler) { _server.Start(handler); }
        public void Stop() { _server.Stop(); }
        public void Dispose() { _server.Dispose(); }
    }

    // =========================================================================
    // STRATEGY 2: White Paper Model (TcpListener AcceptTcpClientAsync + Task.Run + Async Read/Write)
    // =========================================================================
    sealed class PaperServerAdapter : IBenchServer
    {
        readonly bool _allowKeepAlive;
        readonly int _maxConnections;
        TcpListener _listener;
        CancellationTokenSource _cts;
        Action<HttpContext> _handler;
        IPEndPoint _ep;
        int _activeConnections;
        readonly ParserOptions _parserOptions = new ParserOptions();
        readonly RequestSettings _requestSettings = new RequestSettings();

        public string Name
        {
            get
            {
                return _allowKeepAlive
                    ? "2b. Paper Model + Keep-Alive (Task.Run + Async I/O)"
                    : "2a. Paper Model One-Shot (AcceptAsync + Task.Run)";
            }
        }

        public IPEndPoint Endpoint { get { return _ep; } }
        public int ActiveConnections { get { return Volatile.Read(ref _activeConnections); } }

        public PaperServerAdapter(bool allowKeepAlive, int maxConnections)
        {
            _allowKeepAlive = allowKeepAlive;
            _maxConnections = maxConnections;
        }

        public void Start(Action<HttpContext> handler)
        {
            _handler = handler;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start(512);
            _ep = (IPEndPoint)_listener.LocalEndpoint;
            Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                int cur = Interlocked.Increment(ref _activeConnections);
                if (cur > _maxConnections)
                {
                    Interlocked.Decrement(ref _activeConnections);
                    try { client.Close(); } catch { }
                    continue;
                }

                // Exact pattern from the White Paper (Department 1 -> Department 2 via Task.Run)
                var _ = Task.Run(async () =>
                {
                    try
                    {
                        client.NoDelay = true;
                        NetworkStream stream = client.GetStream();
                        byte[] buf = new byte[16 * 1024];
                        int start = 0, end = 0;

                        while (!ct.IsCancellationRequested)
                        {
                            // Department 2: Async Read of request head
                            HttpRequestHead head = null;
                            while (true)
                            {
                                if (end > start)
                                {
                                    HttpParseResult r = HttpRequestParser.ParseHead(buf, start, end - start, _parserOptions, out head);
                                    if (r.Status == HttpParseStatus.Complete) { start += r.Consumed; break; }
                                    if (r.Status == HttpParseStatus.Error) return;
                                }
                                if (start == end) start = end = 0;
                                int n = await stream.ReadAsync(buf, end, buf.Length - end, ct).ConfigureAwait(false);
                                if (n <= 0) return;
                                end += n;
                            }

                            // Department 3: Sync Parse + Sync Handler + Async Write
                            byte[] body = null;
                            int bodyLen = 0;
                            if (head.BodyKind == HttpBodyKind.ContentLength && head.ContentLength > 0)
                            {
                                bodyLen = (int)head.ContentLength;
                                body = new byte[bodyLen];
                                int have = Math.Min(bodyLen, end - start);
                                Buffer.BlockCopy(buf, start, body, 0, have);
                                start += have;
                                while (have < bodyLen)
                                {
                                    int n = await stream.ReadAsync(body, have, bodyLen - have, ct).ConfigureAwait(false);
                                    if (n <= 0) return;
                                    have += n;
                                }
                            }

                            var info = new RequestConnectionInfo
                            {
                                RemoteAddress = IPAddress.Loopback,
                                PeerAddress = IPAddress.Loopback,
                                LocalAddress = IPAddress.Loopback,
                                LocalPort = _ep.Port
                            };
                            var sink = new MemorySink();
                            var req = new HttpRequest(head, body, 0, bodyLen, info, _requestSettings);
                            var resp = new HttpResponse(sink, head);
                            var ctx = new HttpContext(req, resp);

                            HttpContext.Current = ctx;
                            try
                            {
                                _handler(ctx);
                            }
                            finally
                            {
                                HttpContext.Current = null;
                            }

                            if (!_allowKeepAlive)
                            {
                                resp.AppendHeader("Connection", "close");
                            }
                            bool keep = resp.Complete();

                            byte[] outBytes = sink.Buffer;
                            int outLen = sink.Length;
                            await stream.WriteAsync(outBytes, 0, outLen, ct).ConfigureAwait(false);

                            if (!_allowKeepAlive || !keep || !head.KeepAlive) break;
                        }
                    }
                    catch { }
                    finally
                    {
                        // Department 4: Socket Cleanup & Drain Counter
                        try { client.Dispose(); } catch { }
                        Interlocked.Decrement(ref _activeConnections);
                    }
                });
            }
        }

        public void Stop()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
        }

        public void Dispose() { Stop(); }

        sealed class MemorySink : IHttpResponseSink
        {
            readonly MemoryStream _ms = new MemoryStream(512);
            public byte[] Buffer { get { return _ms.GetBuffer(); } }
            public int Length { get { return (int)_ms.Length; } }
            public void Write(byte[] buffer, int offset, int count) { _ms.Write(buffer, offset, count); }
            public void Flush() { }
            public bool IsConnected { get { return true; } }
        }
    }

    // =========================================================================
    // STRATEGY 3: Official cshttp.server 1.0 (Zero-Thread IOCP Idle Wait + Sync Worker Execution)
    // =========================================================================
    sealed class OfficialServer10Adapter : IBenchServer
    {
        readonly HttpServer _server;
        public string Name { get { return "3. cshttp.server 1.0 (0-Thread IOCP Idle + Sync Worker)"; } }
        public IPEndPoint Endpoint { get { return _server.LocalEndpoints[0]; } }
        public int ActiveConnections { get { return _server.ActiveConnections; } }

        public OfficialServer10Adapter(int maxConnections)
        {
            var opts = new ServerOptions
            {
                MaxConnections = maxConnections,
                KeepAliveTimeout = TimeSpan.FromSeconds(60),
                RequestHeadersTimeout = TimeSpan.FromSeconds(30)
            };
            opts.ListenHttp(IPAddress.Loopback, 0);
            _server = new HttpServer(opts);
        }

        public void Start(Action<HttpContext> handler) { _server.Start(handler); }
        public void Stop() { _server.Stop(); }
        public void Dispose() { _server.Dispose(); }
    }
}
