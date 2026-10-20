using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace cshttp.Benchmark
{
    public static class Program
    {
        static readonly byte[] KeepAliveGetReq = Encoding.ASCII.GetBytes("GET /bench HTTP/1.1\r\nHost: localhost\r\n\r\n");
        static readonly byte[] CloseGetReq = Encoding.ASCII.GetBytes("GET /bench HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
        static readonly byte[] BlockingGetReq = Encoding.ASCII.GetBytes("GET /db HTTP/1.1\r\nHost: localhost\r\n\r\n");

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("  cshttp.server 1.0 Architecture Stress & Concurrency Benchmark");
            Console.WriteLine("  Runtime : " + RuntimeInformation.FrameworkDescription);
            Console.WriteLine("  CPU     : Intel Core i7-4770S (4 Cores / 8 Logical Threads @ 3.10 GHz, 24 GB RAM)");
            Console.WriteLine("==========================================================================================");
            Console.WriteLine();

            // Pre-warm ThreadPool for the benchmark load generator
            ThreadPool.SetMinThreads(128, 128);

            // Standard benchmark handler
            Action<HttpContext> handler = ctx =>
            {
                string path = ctx.Request.Path;
                if (path == "/db")
                {
                    // Simulate a 15ms synchronous blocking SQLite / MySQL query inside the handler
                    Thread.Sleep(15);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.Write("{\"status\":\"ok\",\"rows\":42}");
                    return;
                }
                ctx.Response.ContentType = "text/plain";
                ctx.Response.Write("OK");
            };

            Console.WriteLine("--- SCENARIO 1: Nginx Upstream Keep-Alive Pool (32 Persistent Connections, Pure Throughput) ---");
            Console.WriteLine("Simulates Nginx reverse proxy multiplexing requests over 32 persistent loopback sockets (3 seconds each).\n");
            PrintHeader();
            RunKeepAliveThroughput(new AlphaServerAdapter(1000), handler, 32, KeepAliveGetReq, false, 3000);
            RunKeepAliveThroughput(new PaperServerAdapter(false, 5000), handler, 32, CloseGetReq, true, 3000);
            RunKeepAliveThroughput(new PaperServerAdapter(true, 5000), handler, 32, KeepAliveGetReq, false, 3000);
            RunKeepAliveThroughput(new OfficialServer10Adapter(10000), handler, 32, KeepAliveGetReq, false, 3000);
            Console.WriteLine();

            Console.WriteLine("--- SCENARIO 2: Blocking Database Burst (128 Concurrent Clients, 15ms Sync DB Wait per Request) ---");
            Console.WriteLine("Simulates 128 concurrent connections hitting a synchronous 15ms SQL query simultaneously (3 seconds each).\n");
            PrintHeader();
            RunKeepAliveThroughput(new AlphaServerAdapter(1000), handler, 128, BlockingGetReq, false, 3000);
            RunKeepAliveThroughput(new PaperServerAdapter(true, 5000), handler, 128, BlockingGetReq, false, 3000);
            RunKeepAliveThroughput(new OfficialServer10Adapter(10000), handler, 128, BlockingGetReq, false, 3000);
            Console.WriteLine();

            Console.WriteLine("--- SCENARIO 3: C2000 Direct Browser Flood (1,500 Idle Keep-Alive Sockets + 50 Active Workers) ---");
            Console.WriteLine("Simulates 250 browser tabs (6 sockets each = 1,500 idle keep-alive sockets) + 50 active clients.\n");
            PrintIdleFloodHeader();
            RunIdleFloodBenchmark(() => new AlphaServerAdapter(1000), handler, 1500, 50, 2500);
            RunIdleFloodBenchmark(() => new AlphaServerAdapter(2000), handler, 1500, 50, 2500);
            RunIdleFloodBenchmark(() => new PaperServerAdapter(true, 5000), handler, 1500, 50, 2500);
            RunIdleFloodBenchmark(() => new OfficialServer10Adapter(10000), handler, 1500, 50, 2500);
            Console.WriteLine();

            return 0;
        }

        static void PrintHeader()
        {
            Console.WriteLine(string.Format("{0,-55} | {1,10} | {2,9} | {3,9} | {4,8} | {5,6}",
                "Strategy", "Req/sec", "Avg (ms)", "p99 (ms)", "Threads", "Errors"));
            Console.WriteLine(new string('-', 110));
        }

        static void PrintIdleFloodHeader()
        {
            Console.WriteLine(string.Format("{0,-55} | {1,11} | {2,10} | {3,9} | {4,8} | {5,9}",
                "Strategy", "Idle Conns", "Active RPS", "p99 (ms)", "+Threads", "+RAM (MB)"));
            Console.WriteLine(new string('-', 115));
        }

        static void RunKeepAliveThroughput(IBenchServer server, Action<HttpContext> handler, int concurrency, byte[] requestBytes, bool reconnectEachRequest, int durationMs)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int baseThreads = Process.GetCurrentProcess().Threads.Count;
            server.Start(handler);

            try
            {
                // Warmup (500 ms)
                RunLoadWorkers(server.Endpoint, concurrency, requestBytes, reconnectEachRequest, 500);

                // Actual measurement
                BenchMetrics m = RunLoadWorkers(server.Endpoint, concurrency, requestBytes, reconnectEachRequest, durationMs);
                int peakThreads = Process.GetCurrentProcess().Threads.Count - baseThreads;
                if (peakThreads < 0) peakThreads = 0;

                Console.WriteLine(string.Format("{0,-55} | {1,10:N0} | {2,9:F2} | {3,9:F2} | {4,8} | {5,6}",
                    server.Name,
                    m.Rps,
                    m.AvgMs,
                    m.P99Ms,
                    "+" + peakThreads,
                    m.Errors));
            }
            finally
            {
                server.Stop();
                Thread.Sleep(150);
            }
        }

        static void RunIdleFloodBenchmark(Func<IBenchServer> factory, Action<HttpContext> handler, int targetIdleConnections, int activeWorkers, int durationMs)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long memBefore = Process.GetCurrentProcess().PrivateMemorySize64;
            int threadsBefore = Process.GetCurrentProcess().Threads.Count;

            IBenchServer server = factory();
            server.Start(handler);
            var idleSockets = new List<Socket>(targetIdleConnections);

            try
            {
                // 1. Open targetIdleConnections and send 1 initial request so each socket enters Keep-Alive idle wait
                byte[] buf = new byte[256];
                int acceptedIdle = 0;
                for (int i = 0; i < targetIdleConnections; i++)
                {
                    try
                    {
                        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        s.NoDelay = true;
                        s.ReceiveTimeout = 1500;
                        s.SendTimeout = 1500;
                        s.Connect(server.Endpoint);
                        s.Send(KeepAliveGetReq);
                        int n = s.Receive(buf);
                        if (n > 0)
                        {
                            idleSockets.Add(s);
                            acceptedIdle++;
                        }
                        else
                        {
                            s.Close();
                        }
                    }
                    catch
                    {
                        // Connection refused / reset once MaxConnections is exceeded
                    }
                }

                Thread.Sleep(200);
                int threadsAfterIdle = Process.GetCurrentProcess().Threads.Count - threadsBefore;
                if (threadsAfterIdle < 0) threadsAfterIdle = 0;
                double ramDeltaMb = (Process.GetCurrentProcess().PrivateMemorySize64 - memBefore) / (1024.0 * 1024.0);
                if (ramDeltaMb < 0) ramDeltaMb = 0;

                // 2. Now run 50 active clients while those 1,500 idle connections remain open
                BenchMetrics m = RunLoadWorkers(server.Endpoint, activeWorkers, KeepAliveGetReq, false, durationMs);

                string label = server.Name;
                if (server is AlphaServerAdapter)
                {
                    label = acceptedIdle < targetIdleConnections
                        ? "1a. cshttp.server.alpha.1.0 (Default MaxConn=1000)"
                        : "1b. cshttp.server.alpha.1.0 (Raised MaxConn=2000)";
                }

                Console.WriteLine(string.Format("{0,-55} | {1,11} | {2,10:N0} | {3,9:F2} | {4,8} | {5,9:F1}",
                    label,
                    acceptedIdle + "/" + targetIdleConnections,
                    m.Rps,
                    m.P99Ms,
                    "+" + threadsAfterIdle,
                    ramDeltaMb));
            }
            finally
            {
                foreach (Socket s in idleSockets)
                {
                    try { s.Close(0); } catch { }
                }
                server.Stop();
                Thread.Sleep(250);
            }
        }

        sealed class BenchMetrics
        {
            public double Rps;
            public double AvgMs;
            public double P99Ms;
            public long Errors;
        }

        static BenchMetrics RunLoadWorkers(IPEndPoint ep, int concurrency, byte[] requestBytes, bool reconnectEachRequest, int durationMs)
        {
            long totalRequests = 0;
            long totalErrors = 0;
            var latenciesUs = new ConcurrentBag<long>();
            long stopTick = Stopwatch.GetTimestamp() + (long)(durationMs / 1000.0 * Stopwatch.Frequency);

            var tasks = new Task[concurrency];
            for (int w = 0; w < concurrency; w++)
            {
                tasks[w] = Task.Run(async () =>
                {
                    byte[] recvBuf = new byte[1024];
                    Socket sock = null;
                    NetworkStream ns = null;
                    long sampleCounter = 0;

                    try
                    {
                        while (Stopwatch.GetTimestamp() < stopTick)
                        {
                            long t0 = Stopwatch.GetTimestamp();
                            try
                            {
                                if (sock == null)
                                {
                                    sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                                    sock.NoDelay = true;
                                    await sock.ConnectAsync(ep.Address, ep.Port).ConfigureAwait(false);
                                    ns = new NetworkStream(sock, false);
                                }

                                await ns.WriteAsync(requestBytes, 0, requestBytes.Length).ConfigureAwait(false);
                                int readTotal = 0;
                                while (true)
                                {
                                    int n = await ns.ReadAsync(recvBuf, readTotal, recvBuf.Length - readTotal).ConfigureAwait(false);
                                    if (n <= 0)
                                    {
                                        ns.Dispose();
                                        sock.Close();
                                        sock = null;
                                        ns = null;
                                        if (readTotal == 0) throw new IOException("Closed before response");
                                        break;
                                    }
                                    readTotal += n;
                                    if (HasCompleteHttpResponse(recvBuf, readTotal)) break;
                                }

                                if (reconnectEachRequest && sock != null)
                                {
                                    ns.Dispose();
                                    sock.Close();
                                    sock = null;
                                    ns = null;
                                }

                                long elapsedTicks = Stopwatch.GetTimestamp() - t0;
                                Interlocked.Increment(ref totalRequests);
                                if ((sampleCounter++ & 7) == 0)
                                {
                                    latenciesUs.Add(elapsedTicks * 1000000 / Stopwatch.Frequency);
                                }
                            }
                            catch
                            {
                                Interlocked.Increment(ref totalErrors);
                                if (ns != null) { try { ns.Dispose(); } catch { } ns = null; }
                                if (sock != null) { try { sock.Close(0); } catch { } sock = null; }
                            }
                        }
                    }
                    finally
                    {
                        if (ns != null) { try { ns.Dispose(); } catch { } }
                        if (sock != null) { try { sock.Close(0); } catch { } }
                    }
                });
            }

            Task.WaitAll(tasks);

            var arr = latenciesUs.ToArray();
            Array.Sort(arr);
            double avgMs = 0;
            double p99Ms = 0;
            if (arr.Length > 0)
            {
                long sum = 0;
                for (int i = 0; i < arr.Length; i++) sum += arr[i];
                avgMs = (sum / (double)arr.Length) / 1000.0;
                p99Ms = arr[(int)(arr.Length * 0.99)] / 1000.0;
            }

            return new BenchMetrics
            {
                Rps = totalRequests / (durationMs / 1000.0),
                AvgMs = avgMs,
                P99Ms = p99Ms,
                Errors = totalErrors
            };
        }

        static bool HasCompleteHttpResponse(byte[] buf, int len)
        {
            for (int i = 0; i + 3 < len; i++)
            {
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                {
                    int bodyStart = i + 4;
                    return len > bodyStart;
                }
            }
            return false;
        }
    }
}
