using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace cshttp
{
    /// <summary>
    /// High-concurrency, dependency-free HTTP/1.1 server (v2).
    ///
    /// Architecture:
    ///   1. Zero threads held while connections wait for their first request or sit idle in keep-alive
    ///      (uses OS IOCP/epoll via BeginRead on the connection stream).
    ///   2. Synchronous execution once request bytes arrive: header parse, body read, HttpContext.Current,
    ///      and the synchronous Action&lt;HttpContext&gt; handler run straight through on a single worker thread
    ///      with zero async state-machine overhead.
    ///   3. Runs identically on .NET Framework 4.8 and .NET 8+ with zero external NuGet packages.
    /// </summary>
    public sealed class HttpServer : IDisposable
    {
        readonly ServerOptions _o;
        readonly List<Socket> _listeners = new List<Socket>();
        readonly List<Thread> _acceptThreads = new List<Thread>();
        readonly List<IPEndPoint> _endpoints = new List<IPEndPoint>();
        readonly ConcurrentDictionary<Connection, bool> _connections = new ConcurrentDictionary<Connection, bool>();
        readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(false);
        Timer _idleTimer;
        Action<HttpContext> _handler;
        volatile bool _stopping;
        int _started, _stopCalled;
        int _activeConnections, _activeRequests;
        long _totalConnections, _totalRequests;

        public HttpServer(ServerOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            _o = options;
        }

        public ServerOptions Options { get { return _o; } }

        /// <summary>Actual bound endpoints (useful when a binding used port 0).</summary>
        public IList<IPEndPoint> LocalEndpoints { get { return _endpoints.AsReadOnly(); } }

        /// <summary>Open connections (both idle keep-alive and actively processing).</summary>
        public int ActiveConnections { get { return Volatile.Read(ref _activeConnections); } }

        /// <summary>Requests currently executing inside a worker thread.</summary>
        public int ActiveRequests { get { return Volatile.Read(ref _activeRequests); } }

        public long TotalConnections { get { return Interlocked.Read(ref _totalConnections); } }

        public long TotalRequests { get { return Interlocked.Read(ref _totalRequests); } }

        public bool IsStopping { get { return _stopping; } }

        internal Action<HttpContext> Handler { get { return _handler; } }

        /// <summary>Binds all endpoints and starts accepting connections. Returns immediately.</summary>
        public void Start(Action<HttpContext> handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            if (Interlocked.Exchange(ref _started, 1) == 1) throw new InvalidOperationException("The server was already started.");
            if (_o.Bindings.Count == 0) throw new InvalidOperationException("No bindings configured. Call ListenHttp or ListenHttps.");
            _handler = handler;

            EnsureThreadPoolMinThreads();
            HostingEnvironment.BeginHosting(_o.ContentRoot);
            try
            {
                foreach (ServerBinding binding in _o.Bindings)
                {
                    var socket = new Socket(binding.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    if (binding.Address.Equals(IPAddress.IPv6Any)) socket.DualMode = true;
                    socket.Bind(new IPEndPoint(binding.Address, binding.Port));
                    socket.Listen(512);
                    _listeners.Add(socket);
                    _endpoints.Add((IPEndPoint)socket.LocalEndPoint);
                    ServerBinding b = binding;
                    _acceptThreads.Add(new Thread(() => AcceptLoop(socket, b)) { IsBackground = true, Name = "cshttp accept " + b });
                }
            }
            catch
            {
                foreach (Socket s in _listeners) SafeClose(s);
                throw;
            }

            _idleTimer = new Timer(SweepIdleConnections, null, 200, 200);
            foreach (Thread t in _acceptThreads) t.Start();
        }

        /// <summary>Starts the server and blocks until Ctrl+C, process exit or Stop().</summary>
        public void Run(Action<HttpContext> handler)
        {
            Start(handler);
            ConsoleCancelEventHandler onCancel = (s, e) => { e.Cancel = true; ThreadPool.QueueUserWorkItem(_ => Stop()); };
            EventHandler onExit = (s, e) => Stop();
            try { Console.CancelKeyPress += onCancel; } catch { }
            AppDomain.CurrentDomain.ProcessExit += onExit;
            try { _stopped.Wait(); }
            finally
            {
                try { Console.CancelKeyPress -= onCancel; } catch { }
                AppDomain.CurrentDomain.ProcessExit -= onExit;
            }
        }

        /// <summary>Blocks until Stop() has completed.</summary>
        public void WaitForShutdown() { _stopped.Wait(); }

        /// <summary>
        /// Graceful stop: stops accepting, closes idle connections immediately, lets running requests finish
        /// (up to ShutdownTimeout), then waits for background work items.
        /// </summary>
        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopCalled, 1) == 1) { _stopped.Wait(); return; }
            _stopping = true;
            try { if (_idleTimer != null) _idleTimer.Dispose(); } catch { }
            foreach (Socket s in _listeners) SafeClose(s);
            DateTime deadline = DateTime.UtcNow + _o.ShutdownTimeout;
            while (true)
            {
                foreach (Connection c in _connections.Keys) c.CloseIfIdle();
                if (_connections.IsEmpty) break;
                if (DateTime.UtcNow >= deadline)
                {
                    foreach (Connection c in _connections.Keys) c.Abort();
                    break;
                }
                Thread.Sleep(20);
            }
            foreach (Thread t in _acceptThreads) t.Join(2000);
            TimeSpan left = deadline - DateTime.UtcNow;
            if (_started == 1) HostingEnvironment.InitiateShutdown(left > TimeSpan.Zero ? left : TimeSpan.Zero);
            _stopped.Set();
        }

        public void Dispose() { Stop(); }

        void AcceptLoop(Socket listener, ServerBinding binding)
        {
            while (!_stopping)
            {
                Socket client;
                try { client = listener.Accept(); }
                catch (ObjectDisposedException) { break; }
                catch (SocketException ex)
                {
                    if (_stopping) break;
                    LogMessage("Accept failed on " + binding + ": " + ex.Message);
                    Thread.Sleep(10);
                    continue;
                }
                if (_stopping) { SafeClose(client); break; }

                int count = Interlocked.Increment(ref _activeConnections);
                if (count > _o.MaxConnections)
                {
                    Interlocked.Decrement(ref _activeConnections);
                    SafeClose(client);
                    LogMessage("Connection limit (" + _o.MaxConnections + ") reached; connection refused.");
                    continue;
                }

                var conn = new Connection(this, client, binding);
                _connections[conn] = true;
                Interlocked.Increment(ref _totalConnections);
                conn.Start();
            }
        }

        void SweepIdleConnections(object state)
        {
            if (_stopping || _connections.IsEmpty) return;
            long now = Stopwatch.GetTimestamp();
            foreach (Connection c in _connections.Keys)
            {
                c.CheckIdleTimeout(now);
            }
        }

        internal void Remove(Connection c)
        {
            bool ignored;
            if (_connections.TryRemove(c, out ignored))
            {
                Interlocked.Decrement(ref _activeConnections);
            }
        }

        internal void EnterActiveRequest() { Interlocked.Increment(ref _activeRequests); }

        internal void LeaveActiveRequest() { Interlocked.Decrement(ref _activeRequests); }

        internal void CountRequest() { Interlocked.Increment(ref _totalRequests); }

        internal void LogMessage(string message)
        {
            Action<string> log = _o.Log;
            if (log == null) return;
            try { log(message); } catch { }
        }

        internal void ReportError(HttpContext context, Exception ex)
        {
            Action<HttpContext, Exception> h = _o.OnUnhandledException;
            if (h != null)
            {
                try { h(context, ex); } catch { }
            }
            LogMessage("Unhandled exception for " + context.Request.HttpMethod + " " + context.Request.RawUrl + ": " + ex);
        }

        static void EnsureThreadPoolMinThreads()
        {
            try
            {
                int minWorker, minIocp;
                ThreadPool.GetMinThreads(out minWorker, out minIocp);
                int targetWorker = Math.Max(minWorker, Math.Max(256, Environment.ProcessorCount * 32));
                int targetIocp = Math.Max(minIocp, Math.Max(128, Environment.ProcessorCount * 16));
                if (targetWorker > minWorker || targetIocp > minIocp)
                {
                    ThreadPool.SetMinThreads(targetWorker, targetIocp);
                }
            }
            catch { }
        }

        static void SafeClose(Socket s)
        {
            try { s.Close(0); } catch { }
        }
    }
}
