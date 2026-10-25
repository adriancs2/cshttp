using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace cshttp
{
    /// <summary>
    /// Replacement for System.Web.Hosting.HostingEnvironment: application root, MapPath and
    /// background work that is cancelled and awaited on shutdown.
    /// </summary>
    public static class HostingEnvironment
    {
        static readonly object Sync = new object();
        static string _root;
        static CancellationTokenSource _cts = new CancellationTokenSource();
        static int _pending;
        static bool _shuttingDown;

        /// <summary>Called when a background work item throws. Defaults to writing to Console.Error.</summary>
        public static Action<Exception> BackgroundWorkError { get; set; }

        /// <summary>Physical root of the application, always ending with a directory separator.</summary>
        public static string ApplicationPhysicalPath
        {
            get
            {
                string r = _root;
                if (r == null) { SetApplicationPhysicalPath(AppDomain.CurrentDomain.BaseDirectory); r = _root; }
                return r;
            }
        }

        public static string ApplicationVirtualPath { get { return "/"; } }

        public static bool ShutdownInitiated { get { lock (Sync) return _shuttingDown; } }

        public static CancellationToken ShutdownToken { get { lock (Sync) return _cts.Token; } }

        public static int PendingBackgroundWorkItems { get { lock (Sync) return _pending; } }

        public static void SetApplicationPhysicalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
            string full = Path.GetFullPath(path);
            if (full[full.Length - 1] != Path.DirectorySeparatorChar) full += Path.DirectorySeparatorChar;
            _root = full;
        }

        /// <summary>Called by a host when it starts: sets the root (if given) and re-enables background work.</summary>
        public static void BeginHosting(string physicalPath)
        {
            if (!string.IsNullOrEmpty(physicalPath)) SetApplicationPhysicalPath(physicalPath);
            lock (Sync)
            {
                if (_shuttingDown)
                {
                    _shuttingDown = false;
                    _cts = new CancellationTokenSource();
                }
            }
        }

        /// <summary>
        /// Maps "~/x", "/x" or "x" to a physical path under the application root.
        /// Paths that escape the root, or physical paths, throw.
        /// </summary>
        public static string MapPath(string virtualPath)
        {
            string root = ApplicationPhysicalPath;
            string rootNoSep = root.TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "~" || virtualPath == "~/" || virtualPath == "/") return rootNoSep;
            if (virtualPath.IndexOf(':') >= 0) throw new ArgumentException("'" + virtualPath + "' is a physical path, but a virtual path was expected.");
            string v = virtualPath.Replace('\\', '/');
            if (v.StartsWith("~/", StringComparison.Ordinal)) v = v.Substring(2);
            else if (v.StartsWith("/", StringComparison.Ordinal)) v = v.Substring(1);
            string full = Path.GetFullPath(Path.Combine(rootNoSep, v.Replace('/', Path.DirectorySeparatorChar)));
            StringComparison cmp = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(full, rootNoSep, cmp) && !full.StartsWith(root, cmp))
                throw new HttpException(400, "Cannot use a leading .. to exit above the top directory.");
            return full;
        }

        internal static void QueueBackgroundWork(Action<CancellationToken> workItem)
        {
            if (workItem == null) throw new ArgumentNullException("workItem");
            CancellationToken token;
            if (!TryEnter(out token)) return;
            Task.Run(() =>
            {
                try { workItem(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) { Report(ex); }
                finally { Exit(); }
            });
        }

        internal static void QueueBackgroundWork(Func<CancellationToken, Task> workItem)
        {
            if (workItem == null) throw new ArgumentNullException("workItem");
            CancellationToken token;
            if (!TryEnter(out token)) return;
            Task.Run(async () =>
            {
                try { await workItem(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) { Report(ex); }
                finally { Exit(); }
            });
        }

        /// <summary>Stops accepting work, cancels the shutdown token and waits for running items. True if all finished.</summary>
        public static bool InitiateShutdown(TimeSpan timeout)
        {
            CancellationTokenSource cts;
            lock (Sync)
            {
                _shuttingDown = true;
                cts = _cts;
            }
            try { cts.Cancel(); } catch (AggregateException ex) { Report(ex); }
            DateTime deadline = DateTime.UtcNow + timeout;
            lock (Sync)
            {
                while (_pending > 0)
                {
                    TimeSpan left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) break;
                    Monitor.Wait(Sync, left);
                }
                return _pending == 0;
            }
        }

        static bool TryEnter(out CancellationToken token)
        {
            lock (Sync)
            {
                token = _cts.Token;
                if (_shuttingDown) return false;
                _pending++;
                return true;
            }
        }

        static void Exit()
        {
            lock (Sync)
            {
                _pending--;
                Monitor.PulseAll(Sync);
            }
        }

        static void Report(Exception ex)
        {
            Action<Exception> handler = BackgroundWorkError;
            if (handler != null)
            {
                try { handler(ex); } catch { }
            }
            else
            {
                try { Console.Error.WriteLine("[cshttp] Background work item failed: " + ex); } catch { }
            }
        }
    }
}
